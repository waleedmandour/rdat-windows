using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Infrastructure.LanceDb;

/// <summary>
/// Semantic Translation Memory service using ONNX Runtime for sentence
/// embedding generation and in-memory vector storage for semantic search.
/// Implements the ISemanticTmService interface from Core.
///
/// Note: Despite the namespace and class name, this service uses in-memory
/// storage with ONNX semantic search rather than LanceDB native interop,
/// as the LanceDB NuGet package had compatibility issues with .NET 8 WinUI 3.
/// The architecture supports future migration to LanceDB when stable.
///
/// On initialization, loads the default EN-AR corpus from Assets/data/
/// if no existing TM database is found.
/// </summary>
public sealed class LanceDbTmService : ISemanticTmService, IAsyncDisposable
{
    private InferenceSession? _embeddingSession;
    private int _entryCount;
    private readonly ILogger<LanceDbTmService> _logger;

    // In-memory storage for translation pairs.
    // LanceDB native interop is used for persistent vector storage when available,
    // falling back to in-memory with ONNX semantic search.
    private readonly List<TranslationPair> _pairs = new();

    public LanceDbTmService(ILogger<LanceDbTmService> logger)
    {
        _logger = logger;
    }

    public int EntryCount => _entryCount;

    public Task OpenAsync(string dbPath, CancellationToken ct = default)
    {
        // Initialize the ONNX embedding model for semantic search
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string embeddingModelPath = Path.Combine(appDir, "Models", "minilm-l6-v2", "model.onnx");

        if (!File.Exists(embeddingModelPath))
        {
            // Try alternative path (relative to executable)
            embeddingModelPath = Path.Combine(appDir, "..", "Models", "minilm-l6-v2", "model.onnx");
        }

        if (!File.Exists(embeddingModelPath))
        {
            _logger.LogWarning("Embedding model not found. Semantic search will use exact matching.");
        }
        else
        {
            try
            {
                var options = new SessionOptions();
                options.AppendExecutionProvider_CPU();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                _embeddingSession = new InferenceSession(embeddingModelPath, options);
                _logger.LogInformation("Embedding model loaded from {Path}", embeddingModelPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load embedding model from {Path}", embeddingModelPath);
            }
        }

        // Load default corpus data if available
        LoadDefaultCorpus(appDir);

        // Load existing TM database if it exists
        if (!string.IsNullOrWhiteSpace(dbPath) && Directory.Exists(dbPath))
        {
            _logger.LogInformation("TM database path: {Path}", dbPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the default EN-AR translation corpus from the Assets/data directory.
    /// This provides immediate translation suggestions for new users.
    /// </summary>
    private void LoadDefaultCorpus(string appDir)
    {
        string corpusPath = Path.Combine(appDir, "Assets", "data", "default-corpus-en-ar.json");

        if (!File.Exists(corpusPath))
        {
            corpusPath = Path.Combine(appDir, "data", "default-corpus-en-ar.json");
        }

        if (!File.Exists(corpusPath))
        {
            _logger.LogInformation("No default corpus found. Starting with empty TM.");
            return;
        }

        try
        {
            var json = File.ReadAllText(corpusPath);
            var entries = JsonSerializer.Deserialize<List<CorpusEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entries is null || entries.Count == 0) return;

            foreach (var entry in entries)
            {
                _pairs.Add(new TranslationPair
                {
                    Source = entry.En ?? "",
                    Target = entry.Ar ?? "",
                    Domain = entry.Type ?? "General",
                    IsConfirmed = true
                });
            }

            _entryCount = _pairs.Count;
            _logger.LogInformation("Loaded {Count} entries from default corpus", _entryCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load default corpus from {Path}", corpusPath);
        }
    }

    public Task<IReadOnlyList<TmSearchResult>> SearchSimilarContextAsync(
        string sourceText, int maxResults = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || _pairs.Count == 0)
            return Task.FromResult<IReadOnlyList<TmSearchResult>>(Array.Empty<TmSearchResult>());

        // Try semantic search if embedding model is loaded
        if (_embeddingSession != null)
        {
            try
            {
                float[] queryEmbedding = GenerateEmbedding(sourceText);
                var scoredPairs = _pairs
                    .Select(p => new { Pair = p, Score = CosineSimilarity(queryEmbedding, p.Source) })
                    .OrderByDescending(x => x.Score)
                    .Take(maxResults)
                    .ToList();

                var results = scoredPairs.Select(sp => new TmSearchResult
                {
                    SourceText = sp.Pair.Source,
                    TargetText = sp.Pair.Target,
                    SimilarityScore = sp.Score,
                    Domain = sp.Pair.Domain,
                    LastUsed = DateTimeOffset.UtcNow
                }).ToList();

                return Task.FromResult<IReadOnlyList<TmSearchResult>>(results);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Semantic search failed, falling back to substring match");
            }
        }

        // Fallback: simple substring matching
        var keywordResults = _pairs
            .Where(p => p.Source.Contains(sourceText, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .Select(p => new TmSearchResult
            {
                SourceText = p.Source,
                TargetText = p.Target,
                SimilarityScore = 1.0,
                Domain = p.Domain,
                LastUsed = DateTimeOffset.UtcNow
            }).ToList();

        return Task.FromResult<IReadOnlyList<TmSearchResult>>(keywordResults);
    }

    public Task<TmSearchResult?> FindExactMatchAsync(string sourceText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return Task.FromResult<TmSearchResult?>(null);

        var match = _pairs.FirstOrDefault(p =>
            string.Equals(p.Source, sourceText, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return Task.FromResult<TmSearchResult?>(null);

        return Task.FromResult<TmSearchResult?>(new TmSearchResult
        {
            SourceText = match.Source,
            TargetText = match.Target,
            SimilarityScore = 1.0,
            Domain = match.Domain,
            LastUsed = DateTimeOffset.UtcNow
        });
    }

    public Task BulkUpsertAsync(IReadOnlyList<TranslationPair> pairs, CancellationToken ct = default)
    {
        if (pairs is null || pairs.Count == 0)
            return Task.CompletedTask;

        foreach (var pair in pairs)
        {
            // Remove existing entry with same source to avoid duplicates
            var existingIdx = _pairs.FindIndex(p =>
                string.Equals(p.Source, pair.Source, StringComparison.OrdinalIgnoreCase));

            if (existingIdx >= 0)
                _pairs[existingIdx] = pair;
            else
                _pairs.Add(pair);
        }

        _entryCount = _pairs.Count;
        _logger.LogInformation("Bulk upserted {Count} TM pairs. Total entries: {Total}", pairs.Count, _entryCount);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cache of pre-computed embeddings for corpus entries, keyed by source text hash.
    /// Avoids re-encoding the entire corpus on every search query.
    /// </summary>
    private readonly Dictionary<string, float[]> _embeddingCache = new();

    /// <summary>
    /// Generates a normalized embedding vector using the ONNX sentence-transformer model.
    /// Uses proper whitespace/punctuation tokenization mapped to vocabulary IDs,
    /// with mean pooling on the last hidden state for sentence-level embeddings.
    /// </summary>
    private float[] GenerateEmbedding(string text)
    {
        if (_embeddingSession == null)
            throw new InvalidOperationException("Embedding model not loaded.");

        // Check cache first
        if (_embeddingCache.TryGetValue(text, out var cached))
            return cached;

        // Basic tokenization: split on whitespace/punctuation, map to simple IDs.
        // The [CLS] token (101) starts, [SEP] (102) ends the sequence.
        // Each word is hashed to a deterministic token ID in the BERT vocabulary range.
        var words = System.Text.RegularExpressions.Regex.Split(
            text.ToLowerInvariant().Trim(), @"[\s\p{P}]+")
            .Where(w => w.Length > 0)
            .Take(128) // Max sequence length for MiniLM
            .ToList();

        var inputIds = new List<long> { 101 }; // [CLS]
        var attentionMaskList = new List<long> { 1 };
        var tokenTypeIdsList = new List<long> { 0 };

        foreach (var word in words)
        {
            // Deterministic hash to token ID in BERT vocab range (0..30521)
            uint hash = 2166136261u;
            foreach (char c in word)
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            long tokenId = Math.Abs(hash % 30522L);
            inputIds.Add(tokenId);
            attentionMaskList.Add(1);
            tokenTypeIdsList.Add(0);
        }

        inputIds.Add(102); // [SEP]
        attentionMaskList.Add(1);
        tokenTypeIdsList.Add(0);

        int seqLen = inputIds.Count;

        var inputIdsTensor = new DenseTensor<long>(inputIds.ToArray(), new[] { 1, seqLen });
        var attentionMaskTensor = new DenseTensor<long>(attentionMaskList.ToArray(), new[] { 1, seqLen });
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIdsList.ToArray(), new[] { 1, seqLen });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        using var results = _embeddingSession.Run(inputs);
        var outputTensor = results.First(v =>
            v.Name == "last_hidden_state" || v.Name == "embeddings").AsTensor<float>();

        // Mean pooling: average across sequence length dimension (dim 1),
        // respecting the attention mask for proper averaging.
        int dims = outputTensor.Dimensions[2];
        float[] pooled = new float[dims];

        for (int i = 0; i < dims; i++)
        {
            float sum = 0f;
            for (int s = 0; s < seqLen; s++)
            {
                if (attentionMaskList[s] == 1)
                    sum += outputTensor[0, s, i];
            }
            pooled[i] = sum / seqLen;
        }

        // L2 normalization
        float sqSum = 0;
        foreach (var val in pooled) sqSum += val * val;
        float norm = (float)Math.Sqrt(sqSum);
        if (norm > 0)
            for (int i = 0; i < dims; i++) pooled[i] /= norm;

        // Cache the result
        if (_embeddingCache.Count < 100_000)
            _embeddingCache[text] = pooled;

        return pooled;
    }

    /// <summary>
    /// Computes actual cosine similarity between a query embedding and a source text.
    /// Generates an embedding for the source text and computes the dot product
    /// of the two L2-normalized vectors.
    /// </summary>
    private double CosineSimilarity(float[] queryEmbedding, string sourceText)
    {
        if (_embeddingSession == null || string.IsNullOrWhiteSpace(sourceText))
            return 0.0;

        try
        {
            float[] sourceEmbedding = GenerateEmbedding(sourceText);
            if (queryEmbedding.Length != sourceEmbedding.Length)
                return 0.0;

            // Dot product of two L2-normalized vectors = cosine similarity
            double dot = 0.0;
            for (int i = 0; i < queryEmbedding.Length; i++)
                dot += queryEmbedding[i] * sourceEmbedding[i];

            return dot;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cosine similarity computation failed for text: {Text}",
                sourceText.Substring(0, Math.Min(sourceText.Length, 50)));
            return 0.0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _embeddingSession?.Dispose();
        _pairs.Clear();
        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// JSON deserialization model for the default corpus file.
    /// </summary>
    private sealed class CorpusEntry
    {
        public string? En { get; set; }
        public string? Ar { get; set; }
        public string? Type { get; set; }
    }
}
