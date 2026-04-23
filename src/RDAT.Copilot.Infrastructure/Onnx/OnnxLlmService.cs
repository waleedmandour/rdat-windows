using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntimeGenAI;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Infrastructure.Onnx;

/// <summary>
/// Local ONNX GenAI inference service with DirectML GPU acceleration.
/// Implements greedy-decode for low-latency ghost text predictions.
/// </summary>
public sealed class OnnxLlmService : ILlmInferenceService, IAsyncDisposable
{
    private readonly ModelLifetimeScope _modelScope;
    private readonly ILogger<OnnxLlmService> _logger;
    private bool _isLoaded;
    private TimeSpan _lastLatency;

    private const int DefaultMaxNewTokens = 64;
    private const int DefaultMaxStreamingTokens = 2048;

    public OnnxLlmService(ILogger<OnnxLlmService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelScope = new ModelLifetimeScope();
    }

    public bool IsModelLoaded => _isLoaded;

    public string InferenceMode => _isLoaded ? "DirectML GPU" : "Not Loaded";

    /// <summary>
    /// Gets the model path that was last used to load the model, or null if not loaded.
    /// </summary>
    public string? LoadedModelPath { get; private set; }

    public Task LoadModelAsync(string modelPath, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            await _modelScope.LoadAsync(modelPath, ct);
            _isLoaded = true;
            LoadedModelPath = modelPath;
            _logger.LogInformation("ONNX LLM model loaded from {ModelPath}", modelPath);
        }, ct);
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
            throw new InvalidOperationException("Model not loaded. Call LoadModelAsync first.");

        _modelScope.AcquireLock();
        try
        {
            var tokenizer = _modelScope.AcquireTokenizer();
            var model = _modelScope.AcquireModel();

            var prompt = BuildTranslationPrompt(sourceText, sourceLang, targetLang);
            using var tokens = tokenizer.Encode(prompt);
            using var generatorParams = new GeneratorParams(model);
            generatorParams.SetSearchOption("max_length", DefaultMaxStreamingTokens);
            generatorParams.SetInputSequences(tokens);

            using var generator = new Generator(model, generatorParams);

            while (!generator.IsDone())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();

                generator.ComputeLogits();
                generator.GenerateNextToken();

                int token = generator.GetSequence(0)[^1];
                string decoded = tokenizer.Decode(new[] { token });
                yield return decoded;
            }
        }
        finally
        {
            _modelScope.ReleaseInferenceHandle();
        }
    }

    public async Task<GhostTextResult> GetPredictionAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
            return new GhostTextResult { Text = "", Confidence = 0 };

        var sw = Stopwatch.StartNew();

        _modelScope.AcquireLock();
        try
        {
            var tokenizer = _modelScope.AcquireTokenizer();
            var model = _modelScope.AcquireModel();

            string prompt = BuildTranslationPrompt(sourceText, sourceLang, targetLang);

            using var tokens = tokenizer.Encode(prompt);
            using var generatorParams = new GeneratorParams(model);

            generatorParams.SetSearchOption("max_length", DefaultMaxNewTokens);
            generatorParams.SetSearchOption("top_p", 1.0);
            generatorParams.SetSearchOption("top_k", 1);
            generatorParams.SetSearchOption("temperature", 0.0);
            generatorParams.SetInputSequences(tokens);

            using var generator = new Generator(model, generatorParams);

            var generatedTokens = new List<int>(DefaultMaxNewTokens);
            int generatedCount = 0;

            while (!generator.IsDone() && generatedCount < DefaultMaxNewTokens)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new GhostTextResult { Text = "", Confidence = 0 };

                generator.ComputeLogits();
                generator.GenerateNextToken();

                int token = generator.GetSequence(0)[^1];
                generatedTokens.Add(token);
                generatedCount++;
            }

            string prediction = tokenizer.Decode(generatedTokens.ToArray());
            sw.Stop();
            _lastLatency = sw.Elapsed;

            if (string.IsNullOrWhiteSpace(prediction))
                return new GhostTextResult { Text = "", Confidence = 0, LatencyMs = (long)sw.Elapsed.TotalMilliseconds };

            return new GhostTextResult
            {
                Text = prediction,
                Confidence = 1.0,
                LatencyMs = (long)_lastLatency.TotalMilliseconds,
                Source = "local",
                IsSuppressed = false
            };
        }
        catch (OperationCanceledException)
        {
            return new GhostTextResult { Text = "", Confidence = 0 };
        }
        finally
        {
            _modelScope.ReleaseInferenceHandle();
        }
    }

    private static string BuildTranslationPrompt(string sourceText, string sourceLang, string targetLang)
    {
        var sb = new StringBuilder();
        sb.Append($"Instruct: Translate the following text from {sourceLang} to {targetLang}.\n");
        sb.Append($"Source: {sourceText}\n");
        sb.Append("Target:");
        return sb.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await _modelScope.DisposeAsync();
    }
}
