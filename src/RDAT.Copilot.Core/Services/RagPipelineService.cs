using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// RAG (Retrieval-Augmented Generation) pipeline orchestrator.
/// Coordinates the embedding service and vector database to provide
/// real-time TM search for ghost text suggestions.
///
/// Pipeline flow:
///   1. Source sentence → OnnxEmbeddingService.EmbedAsync() → 384-dim vector
///   2. Vector → LanceVectorDbService.SearchAsync() → top-K TM matches
///   3. Matches → ranked TmSearchResult[] → ghost text channel / TM panel
///
/// Performance target: <50ms end-to-end for a single sentence query
/// on a database of 10M+ entries.
/// </summary>
public sealed class RagPipelineService : IRagPipelineService, IDisposable
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorDatabaseService _vectorDb;
    private readonly ITmImportService _importService;
    private readonly ILogger<RagPipelineService> _logger;

    private int _initialized;
    private string? _dbPath;

    public RagState State { get; private set; } = RagState.Idle;

    public long TotalTmCount { get; private set; }

    public bool IsReady => Volatile.Read(ref _initialized) != 0 && _embeddingService.IsReady && _vectorDb.IsReady;

    public RagPipelineService(
        IEmbeddingService embeddingService,
        IVectorDatabaseService vectorDb,
        ITmImportService importService,
        ILogger<RagPipelineService> logger)
    {
        _embeddingService = embeddingService;
        _vectorDb = vectorDb;
        _importService = importService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(
        string modelPath,
        string dbPath,
        IProgress<(double Progress, string Text)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized) != 0) return;

        _dbPath = dbPath;
        State = RagState.LoadingModel;

        try
        {
            // Step 1: Load embedding model
            progress?.Report((0.0, "Loading embedding model..."));
            _logger.LogInformation("[RAG] Initializing embedding model from: {Path}", modelPath);

            await _embeddingService.InitializeAsync(modelPath, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report((0.4, "Embedding model loaded."));

            // Step 2: Open vector database
            State = RagState.Indexing;
            progress?.Report((0.5, "Opening vector database..."));
            _logger.LogInformation("[RAG] Opening vector database at: {Path}", dbPath);

            await _vectorDb.OpenAsync(dbPath, cancellationToken).ConfigureAwait(false);
            progress?.Report((0.7, "Vector database opened."));

            // Step 3: Check if TM table exists and get count
            TotalTmCount = await _vectorDb.CountAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report((0.9, $"TM database ready: {TotalTmCount:N0} entries."));

            Volatile.Write(ref _initialized, 1);
            State = RagState.Ready;

            _logger.LogInformation(
                "[RAG] Pipeline initialized: {Count:N0} TM entries indexed, " +
                "embedding model ready ({Dim} dimensions)",
                TotalTmCount, AppConstants.EmbeddingDimensions);
        }
        catch (Exception ex)
        {
            State = RagState.Error;
            _logger.LogError(ex, "[RAG] Failed to initialize pipeline");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TmSearchResult>> SearchTmAsync(
        string sourceText,
        int topK = 5,
        double minimumScore = 0.5,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized) == 0)
            throw new InvalidOperationException("RAG pipeline is not initialized.");

        if (string.IsNullOrWhiteSpace(sourceText))
            return Array.Empty<TmSearchResult>();

        State = RagState.Searching;
        var sw = Stopwatch.StartNew();

        try
        {
            // Step 1: Generate embedding for query
            var queryEmbedding = await _embeddingService.EmbedAsync(sourceText, cancellationToken).ConfigureAwait(false);

            // Step 2: Search vector database
            var rawResults = await _vectorDb.SearchAsync(queryEmbedding, topK, cancellationToken).ConfigureAwait(false);

            // Step 3: Filter by minimum score and convert to TmSearchResult
            var results = rawResults
                .Where(r => r.Score >= minimumScore)
                .Select(r => new TmSearchResult(
                    Entry: new TmEntry(
                        r.Id,
                        r.SourceText,
                        r.TargetText
                    ),
                    Score: r.Score,
                    SearchMs: r.SearchMilliseconds
                ))
                .ToList();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;

            _logger.LogDebug(
                "[RAG] Search for \"{Query}\" → {Count} results in {Ms:F1}ms (top score: {BestScore:F3})",
                sourceText.Length > 50 ? sourceText[..50] + "..." : sourceText,
                results.Count,
                elapsedMs,
                results.FirstOrDefault()?.Score ?? 0.0);

            State = RagState.Ready;
            return results;
        }
        catch (Exception ex)
        {
            State = RagState.Ready;
            _logger.LogError(ex, "[RAG] Search failed for: {Query}", sourceText[..Math.Min(50, sourceText.Length)]);
            return Array.Empty<TmSearchResult>();
        }
    }

    /// <inheritdoc/>
    public async Task<TmSearchResult?> GetBestMatchAsync(string sourceText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        var results = await SearchTmAsync(sourceText, topK: 1, minimumScore: 0.7, cancellationToken: cancellationToken).ConfigureAwait(false);
        return results.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<TmImportResult> ImportTmFileAsync(
        string filePath,
        string sourceLanguage = "en",
        string targetLanguage = "ar",
        string? domain = null,
        IProgress<(int Imported, int Total, string Text)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"TM file not found: {filePath}", filePath);

        var sw = Stopwatch.StartNew();
        var errors = new List<string>();

        _logger.LogInformation(
            "[RAG] Starting TM import: {File} ({Source}→{Target})",
            Path.GetFileName(filePath), sourceLanguage, targetLanguage);

        State = RagState.Indexing;

        try
        {
            // Step 1: Parse the file
            progress?.Report((0, 100, "Parsing TM file..."));
            var entries = await _importService.ParseAsync(
                filePath, sourceLanguage, targetLanguage, domain, cancellationToken)
                .ConfigureAwait(false);

            var totalRows = entries.Count;
            var importedCount = 0;
            var skippedCount = 0;

            if (totalRows == 0)
            {
                return new TmImportResult(0, 0, 0, 0, errors, sw.Elapsed.TotalMilliseconds);
            }

            // Step 2: Generate embeddings in batches (to avoid OOM on large TMs)
            const int batchSize = 100;
            var batchCount = (int)Math.Ceiling(totalRows / (double)batchSize);

            progress?.Report((0, totalRows, $"Generating embeddings for {totalRows:N0} entries..."));

            for (int batchIdx = 0; batchIdx < batchCount; batchIdx++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var skip = batchIdx * batchSize;
                var batch = entries.Skip(skip).Take(batchSize).ToList();
                var texts = batch.Select(e => e.SourceText).ToArray();

                float[][] embeddings;
                try
                {
                    embeddings = await _embeddingService.EmbedBatchAsync(texts).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RAG] Embedding failed for batch {Idx}", batchIdx);
                    errors.Add($"Batch {batchIdx}: Embedding generation failed — {ex.Message}");
                    skippedCount += batch.Count;
                    continue;
                }

                // Step 3: Index the batch into LanceDB
                var indexEntries = batch.Zip(embeddings, (entry, embedding) => (
                    Id: entry.Id,
                    SourceText: entry.SourceText,
                    TargetText: entry.TargetText,
                    Embedding: embedding
                )).ToList();

                try
                {
                    await _vectorDb.IndexBatchAsync(indexEntries).ConfigureAwait(false);
                    importedCount += batch.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RAG] Indexing failed for batch {Idx}", batchIdx);
                    errors.Add($"Batch {batchIdx}: Vector indexing failed — {ex.Message}");
                    skippedCount += batch.Count;
                }

                progress?.Report((importedCount, totalRows,
                    $"Indexed {importedCount:N0} / {totalRows:N0} entries..."));
            }

            // Step 4: Update count
            TotalTmCount = await _vectorDb.CountAsync().ConfigureAwait(false);

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            State = RagState.Ready;

            _logger.LogInformation(
                "[RAG] Import complete: {Imported}/{Total} entries in {Ms:F0}ms " +
                "({Skipped} skipped, {Errors} errors)",
                importedCount, totalRows, elapsedMs, skippedCount, errors.Count);

            return new TmImportResult(
                TotalRows: totalRows,
                ImportedCount: importedCount,
                SkippedCount: skippedCount,
                ErrorCount: errors.Count,
                Errors: errors,
                ElapsedMs: elapsedMs
            );
        }
        catch (OperationCanceledException)
        {
            State = RagState.Ready;
            _logger.LogWarning("[RAG] TM import cancelled");
            throw;
        }
        catch (Exception ex)
        {
            State = RagState.Error;
            _logger.LogError(ex, "[RAG] TM import failed");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<TmStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var count = await _vectorDb.CountAsync(cancellationToken).ConfigureAwait(false);
        var dbSizeMb = _dbPath is not null
            ? await GetDbSizeMbAsync(_dbPath).ConfigureAwait(false)
            : 0;

        return new TmStats(
            TotalEntries: count,
            DomainBreakdown: new Dictionary<string, int>(),
            LanguagePairs: new Dictionary<string, int>
            {
                { "en→ar", (int)count },
                { "ar→en", 0 }
            },
            DbSizeMb: dbSizeMb
        );
    }

    /// <inheritdoc/>
    public async Task ShutdownAsync()
    {
        _logger.LogInformation("[RAG] Shutting down pipeline...");
        await _vectorDb.CloseAsync().ConfigureAwait(false);
        Volatile.Write(ref _initialized, 0);
        State = RagState.Idle;
        TotalTmCount = 0;
    }

    private static async Task<double> GetDbSizeMbAsync(string dbPath)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(dbPath)) return 0;
            var dirInfo = new DirectoryInfo(dbPath);
            var totalBytes = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            return totalBytes / (1024.0 * 1024.0);
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_vectorDb is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
