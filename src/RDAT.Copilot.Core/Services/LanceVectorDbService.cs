using System.Diagnostics;
using lancedb;
using Apache.Arrow;
using Apache.Arrow.Types;
using Table = lancedb.Table;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// LanceDB-backed vector database service for persistent Translation Memory storage.
/// Uses disk-based LanceDB tables for efficient similarity search over 10M+ entries
/// without loading the entire dataset into RAM.
///
/// Storage format: LanceDB table with columns [id, source_text, target_text,
/// source_language, target_language, domain, quality_score, created_at_unix, vector]
/// where vector is a FixedSizeList[float] column for the embedding.
///
/// Requires: LanceDB 2.x NuGet package (lancedb-csharp community SDK).
/// All data operations use Apache Arrow RecordBatch for zero-copy Rust interop.
/// </summary>
public sealed class LanceVectorDbService : IVectorDatabaseService, IDisposable
{
    private readonly ILogger<LanceVectorDbService>? _logger;
    private readonly string _tableName = "translation_memory";
    private readonly int _embeddingDim = AppConstants.EmbeddingDimensions;
    private Connection? _connection;
    private lancedb.Table? _table;
    private bool _disposed;

    public LanceVectorDbService(ILogger<LanceVectorDbService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsReady => _table is not null;

    /// <inheritdoc/>
    public async Task OpenAsync(string dbPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbPath);

        Directory.CreateDirectory(dbPath);

        _connection = new Connection();
        await _connection.Connect(dbPath).ConfigureAwait(false);

        // Try to open existing table; if not found, table will be created on first IndexBatchAsync
        try
        {
            _table = await _connection.OpenTable(_tableName).ConfigureAwait(false);
            _logger?.LogDebug("[LanceDB] Opened existing table: {TableName}", _tableName);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[LanceDB] Table '{TableName}' not found — will be created on first index", _tableName);
            _table = null;
        }
    }

    /// <inheritdoc/>
    public async Task IndexBatchAsync(
        IEnumerable<(string Id, string SourceText, string TargetText, float[] Embedding)> entries,
        CancellationToken cancellationToken = default)
    {
        if (_connection is null)
            throw new InvalidOperationException("Database is not open. Call OpenAsync first.");

        var entryList = entries.ToList();
        if (entryList.Count == 0) return;

        var recordBatch = BuildRecordBatch(entryList);

        if (_table is null)
        {
            // First-time: create table with initial data
            _logger?.LogDebug("[LanceDB] Creating table {TableName} with {Count} entries", _tableName, entryList.Count);
            _table = await _connection.CreateTable(_tableName, recordBatch).ConfigureAwait(false);
        }
        else
        {
            // Append to existing table
            try
            {
                await _table.Add(recordBatch).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug(ex, "[LanceDB] Table not found — creating new table: {TableName}", _tableName);
                _table = await _connection.CreateTable(_tableName, recordBatch).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (_table is null)
            throw new InvalidOperationException("Database is not open. Call OpenAsync first.");

        if (queryEmbedding is null || queryEmbedding.Length == 0)
            return System.Array.Empty<VectorSearchResult>();

        var sw = Stopwatch.StartNew();

        // NearestTo requires double[] in LanceDB 2.x
        var queryVector = new double[queryEmbedding.Length];
        for (int i = 0; i < queryEmbedding.Length; i++)
            queryVector[i] = queryEmbedding[i];

        var results = await _table!.Query()
            .NearestTo(queryVector)
            .DistanceType(DistanceType.Cosine)
            .Limit(topK)
            .ToList()
            .ConfigureAwait(false);

        var elapsedMs = sw.Elapsed.TotalMilliseconds;

        return results.Select(row =>
        {
            // LanceDB returns distance under "_distance" key
            var score = row.TryGetValue("_distance", out var distVal)
                ? Convert.ToSingle(distVal)
                : 0f;

            var sourceText = row.TryGetValue("source_text", out var st)
                ? st?.ToString() ?? string.Empty
                : string.Empty;

            var targetText = row.TryGetValue("target_text", out var tt)
                ? tt?.ToString() ?? string.Empty
                : string.Empty;

            var id = row.TryGetValue("id", out var idVal)
                ? idVal?.ToString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();

            return new VectorSearchResult(
                Id: id,
                SourceText: sourceText,
                TargetText: targetText,
                Score: score,
                SearchMilliseconds: elapsedMs
            );
        }).ToList();
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        if (_table is null) return 0;

        try
        {
            return await _table!.CountRows().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[LanceDB] Failed to count rows");
            return 0L;
        }
    }

    /// <inheritdoc/>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _table?.Dispose();
        _table = null;
        _connection?.Dispose();
        _connection = null;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Create a new table with the given initial data.
    /// Used during first-time TM import.
    /// </summary>
    public async Task CreateTableAsync(
        IEnumerable<LanceTmRow> rows,
        IEnumerable<float[]> vectors,
        CancellationToken cancellationToken = default)
    {
        if (_connection is null)
            throw new InvalidOperationException("Database is not open.");

        var rowList = rows.ToList();
        var vectorList = vectors.ToList();

        if (rowList.Count != vectorList.Count)
            throw new ArgumentException($"Row count ({rowList.Count}) must match vector count ({vectorList.Count}).");

        var entries = rowList.Select((r, i) => (r.Id, r.SourceText, r.TargetText, vectorList[i])).ToList();
        var recordBatch = BuildRecordBatch(entries, rowList);

        _logger?.LogDebug("[LanceDB] Creating table {TableName} with {Count} entries from LanceTmRow data",
            _tableName, rowList.Count);
        _table = await _connection!.CreateTable(_tableName, recordBatch).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if the TM table exists in the database.
    /// </summary>
    public async Task<bool> TableExistsAsync()
    {
        if (_connection is null) return false;

        try
        {
            _table = await _connection!.OpenTable(_tableName).ConfigureAwait(false);
            return true;
        }
        catch
        {
            _table = null;
            return false;
        }
    }

    /// <summary>
    /// Get the database file size on disk.
    /// </summary>
    public async Task<double> GetDbSizeMbAsync(string dbPath)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(dbPath)) return 0;

            var dirInfo = new DirectoryInfo(dbPath);
            var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);
            var totalBytes = files.Sum(f => f.Length);

            return totalBytes / (1024.0 * 1024.0);
        }).ConfigureAwait(false);
    }

    // ─── Apache Arrow Schema & RecordBatch Helpers ────────────────────────────

    /// <summary>
    /// Builds the LanceDB table schema for Translation Memory entries.
    /// Vector column uses FixedSizeList[float] with dimension from AppConstants.EmbeddingDimensions.
    /// </summary>
    private Schema BuildTmSchema()
    {
        var vectorField = new Field("vector", FloatType.Default, nullable: false);
        var vectorType = new FixedSizeListType(vectorField, _embeddingDim);

        return new Schema.Builder()
            .Field(new Field("id", StringType.Default, nullable: false))
            .Field(new Field("source_text", StringType.Default, nullable: false))
            .Field(new Field("target_text", StringType.Default, nullable: false))
            .Field(new Field("source_language", StringType.Default, nullable: false))
            .Field(new Field("target_language", StringType.Default, nullable: false))
            .Field(new Field("domain", StringType.Default, nullable: true))
            .Field(new Field("quality_score", DoubleType.Default, nullable: false))
            .Field(new Field("created_at_unix", Int64Type.Default, nullable: false))
            .Field(new Field("vector", vectorType, nullable: false))
            .Build();
    }

    /// <summary>
    /// Converts a list of (Id, SourceText, TargetText, Embedding) tuples
    /// into an Apache Arrow RecordBatch matching the TM schema.
    /// </summary>
    private RecordBatch BuildRecordBatch(
        List<(string Id, string SourceText, string TargetText, float[] Embedding)> entries,
        List<LanceTmRow>? tmRows = null)
    {
        var count = entries.Count;
        var schema = BuildTmSchema();

        // Scalar column builders
        var idBuilder = new StringArray.Builder();
        var sourceTextBuilder = new StringArray.Builder();
        var targetTextBuilder = new StringArray.Builder();
        var sourceLanguageBuilder = new StringArray.Builder();
        var targetLanguageBuilder = new StringArray.Builder();
        var domainBuilder = new StringArray.Builder();
        var qualityScoreBuilder = new DoubleArray.Builder();
        var createdAtUnixBuilder = new Int64Array.Builder();

        // Vector column: flatten all embeddings into a single FloatArray
        var vectorValuesBuilder = new FloatArray.Builder();

        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];
            idBuilder.Append(entry.Id);
            sourceTextBuilder.Append(entry.SourceText);
            targetTextBuilder.Append(entry.TargetText);

            if (tmRows is { Count: > 0 } && i < tmRows.Count)
            {
                var tm = tmRows[i];
                sourceLanguageBuilder.Append(tm.SourceLanguage);
                targetLanguageBuilder.Append(tm.TargetLanguage);
                domainBuilder.Append(tm.Domain);
                qualityScoreBuilder.Append(tm.QualityScore);
                createdAtUnixBuilder.Append(tm.CreatedAtUnix);
            }
            else
            {
                sourceLanguageBuilder.Append("en");
                targetLanguageBuilder.Append("ar");
                domainBuilder.AppendNull();
                qualityScoreBuilder.Append(1.0);
                createdAtUnixBuilder.Append(DateTime.UtcNow.Ticks);
            }

            var emb = entry.Embedding;
            for (int d = 0; d < _embeddingDim; d++)
            {
                vectorValuesBuilder.Append(d < emb.Length ? emb[d] : 0f);
            }
        }

        // Build scalar arrays
        var idArray = idBuilder.Build();
        var sourceTextArray = sourceTextBuilder.Build();
        var targetTextArray = targetTextBuilder.Build();
        var sourceLangArray = sourceLanguageBuilder.Build();
        var targetLangArray = targetLanguageBuilder.Build();
        var domainArray = domainBuilder.Build();
        var qualityArray = qualityScoreBuilder.Build();
        var createdAtArray = createdAtUnixBuilder.Build();

        // Build vector column as FixedSizeListArray
        var vectorValues = vectorValuesBuilder.Build();
        var vectorField = new Field("vector", FloatType.Default, nullable: false);
        var vectorType = new FixedSizeListType(vectorField, _embeddingDim);
        var vectorArray = new FixedSizeListArray(vectorType, count, vectorValues, ArrowBuffer.Empty, 0, 0);

        return new RecordBatch(schema, new IArrowArray[]
        {
            idArray,
            sourceTextArray,
            targetTextArray,
            sourceLangArray,
            targetLangArray,
            domainArray,
            qualityArray,
            createdAtArray,
            vectorArray
        }, count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _table?.Dispose();
        _table = null;
        _connection?.Dispose();
        _connection = null;
    }
}
