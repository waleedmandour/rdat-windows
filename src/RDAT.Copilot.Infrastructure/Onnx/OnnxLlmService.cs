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
using RDAT.Copilot.Core.Services;

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

    public OnnxLlmService(ILogger<OnnxLlmService> logger, ILogger<ModelLifetimeScope> scopeLogger)
    {
        _logger = logger;
        _modelScope = new ModelLifetimeScope(scopeLogger);
    }

    public bool IsModelLoaded => _isLoaded;

    public string InferenceMode => _isLoaded ? "DirectML GPU" : "Not Loaded";

    public Task LoadModelAsync(string modelPath, CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            await _modelScope.LoadAsync(modelPath, ct);
            _isLoaded = true;
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
            generatorParams.SetSearchOption("max_length", tokens.SequenceLength + DefaultMaxStreamingTokens);
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

        var sw = ValueStopwatch.StartNew();

        _modelScope.AcquireLock();
        try
        {
            var tokenizer = _modelScope.AcquireTokenizer();
            var model = _modelScope.AcquireModel();

            // Minimal prompt optimized for ghost text latency
            string prompt = BuildTranslationPrompt(sourceText, sourceLang, targetLang);

            using var tokens = tokenizer.Encode(prompt);
            using var generatorParams = new GeneratorParams(model);

            // Greedy search: top_p=1, top_k=1, temperature=0
            generatorParams.SetSearchOption("max_length", tokens.SequenceLength + DefaultMaxNewTokens);
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
            _lastLatency = sw.GetElapsedTime();

            if (string.IsNullOrWhiteSpace(prediction))
                return new GhostTextResult { Text = "", Confidence = 0, LatencyMs = _lastLatency.Milliseconds };

            return new GhostTextResult
            {
                Text = prediction,
                Confidence = 1.0, // Greedy decode gives deterministic output
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

    private file struct ValueStopwatch
    {
        private readonly long _startTimestamp;

        private ValueStopwatch(long startTimestamp) => _startTimestamp = startTimestamp;

        public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

        public TimeSpan GetElapsedTime() => Stopwatch.GetElapsedTime(_startTimestamp);
    }
}
