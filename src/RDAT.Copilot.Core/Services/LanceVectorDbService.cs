using System.Collections.Concurrent;
using System.Diagnostics;
using LanceDB;
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
/// source_language, target_language, domain, quality_score, created_at_unix]
/// plus a vector column for the embedding.
/// </summary>
public sealed class LanceVectorDbService : IVectorDatabaseService, IDisposable
{
    private readonly ILogger<LanceVectorDbService>? _logger;
    private readonly string _tableName = "translation_memory";
    private Connection? _connection;
    private LanceTable? _table;
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

        await Task.Run(() =>
        {
            Directory.CreateDirectory(dbPath);
            _connection = new LanceDBConnection(dbPath);
            _table = _connection.OpenTable(_tableName);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task IndexBatchAsync(IEnumerable<(string Id, string SourceText, string TargetText, float[] Embedding)> entries, CancellationToken cancellationToken = default)
    {
        if (_connection is null)
            throw new InvalidOperationException("Database is not open. Call OpenAsync first.");

        var entryList = entries.ToList();
        if (entryList.Count == 0) return;

        await Task.Run(() =>
        {
            var rows = entryList.Select(e => new LanceTmRow
            {
                Id = e.Id,
                SourceText = e.SourceText,
                TargetText = e.TargetText,
                CreatedAtUnix = DateTime.UtcNow.Ticks
            }).ToList();

            // Build embedding vectors for LanceDB
            var vectors = entryList.Select(e => e.Embedding).ToList();

            try
            {
                // Try to add to existing table
                _table!.Add(rows, vectors);
            }
            catch (Exception ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug(ex, "[LanceDB] Table not found — creating new table: {TableName}", _tableName);
                _table = _connection!.CreateTable(_tableName, rows, vectors);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (_table is null)
            throw new InvalidOperationException("Database is not open. Call OpenAsync first.");

        if (queryEmbedding is null || queryEmbedding.Length == 0)
            return Array.Empty<VectorSearchResult>();

        var sw = Stopwatch.StartNew();

        var results = await Task.Run(() =>
        {
            var query = _table!.Search(queryEmbedding)
                .Limit(topK)
                .ToResultSet();

            return query.Rows.Select(row =>
            {
                var score = row.Score;
                var sourceText = row["source_text"]?.ToString() ?? string.Empty;
                var targetText = row["target_text"]?.ToString() ?? string.Empty;
                var id = row["id"]?.ToString() ?? Guid.NewGuid().ToString();

                return new VectorSearchResult(
                    Id: id,
                    SourceText: sourceText,
                    TargetText: targetText,
                    Score: score,
                    SearchMilliseconds: sw.Elapsed.TotalMilliseconds
                );
            }).ToList();
        }, cancellationToken).ConfigureAwait(false);

        return results;
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        if (_table is null) return 0;

        return await Task.Run(() =>
        {
            try
            {
                var countResult = _table!.CountRows();
                return (long)countResult;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[LanceDB] Failed to count rows");
                return 0L;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            _table = null;
            _connection = null;
        }, cancellationToken).ConfigureAwait(false);
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

        await Task.Run(() =>
        {
            var rowList = rows.ToList();
            var vectorList = vectors.ToList();
            _table = _connection!.CreateTable(_tableName, rowList, vectorList);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Check if the TM table exists in the database.
    /// </summary>
    public async Task<bool> TableExistsAsync()
    {
        if (_connection is null) return false;

        return await Task.Run(() =>
        {
            try
            {
                _table = _connection!.OpenTable(_tableName);
                return true;
            }
            catch
            {
                _table = null;
                return false;
            }
        }).ConfigureAwait(false);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _table = null;
        _connection = null;
    }
}
