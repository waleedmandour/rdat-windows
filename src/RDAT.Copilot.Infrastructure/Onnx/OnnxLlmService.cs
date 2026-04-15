using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntimeGenAI;
using RDAT.Copilot.Core.Models;
using RDAT.Copilot.Core.Services;

namespace RDAT.Copilot.Infrastructure.Onnx;

public sealed class OnnxLlmService : ILlmInferenceService, IAsyncDisposable
{
    private readonly ModelLifetimeScope _modelScope;
    private readonly ILogger<OnnxLlmService> _logger;
    private bool _isLoaded;
    private TimeSpan _lastLatency;

    public OnnxLlmService(ILogger<OnnxLlmService> logger, ILogger<ModelLifetimeScope> scopeLogger)
    {
        _logger = logger;
        _modelScope = new ModelLifetimeScope(scopeLogger);
    }

    public bool IsModelLoaded => _isLoaded;

    public InferenceBackend ActiveBackend => InferenceBackend.LocalDirectMl;

    public TimeSpan LastPredictionLatency => _lastLatency;

    public async ValueTask LoadModelAsync(string modelDirectory, CancellationToken cancellationToken = default)
    {
        await _modelScope.LoadAsync(modelDirectory, cancellationToken);
        _isLoaded = true;
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        TranslationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_isLoaded) throw new InvalidOperationException("Model not loaded yet.");

        _modelScope.AcquireLock();
        try
        {
            var tokenizer = _modelScope.AcquireTokenizer();
            var model = _modelScope.AcquireModel();

            // Construct prompt utilizing context, source, and language directions
            var prompt = $"Instruct: Translate the following text from {request.SourceLanguage} to {request.TargetLanguage}.\n";
            prompt += $"Source: {request.SourceText}\nTarget:";

            using var tokens = tokenizer.Encode(prompt);
            using var generatorParams = new GeneratorParams(model);
            generatorParams.SetSearchOption("max_length", 2048);
            generatorParams.SetInputSequences(tokens);

            using var generator = new Generator(model, generatorParams);

            while (!generator.IsDone())
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Allow thread to yield explicitly if generation takes long
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

    public async Task<GhostTextResult?> GetPredictionAsync(
        string partialSourceText,
        string partialTargetText,
        string tmContext,
        PredictionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isLoaded) return null;

        var opt = options ?? PredictionOptions.Default;
        var sw = ValueStopwatch.StartNew();

        _modelScope.AcquireLock();
        try
        {
            var tokenizer = _modelScope.AcquireTokenizer();
            var model = _modelScope.AcquireModel();

            // Minimal prompt to satisfy Ghost text needs
            string prompt = $"{tmContext}\nSource: {partialSourceText}\nTarget: {partialTargetText}";
            
            using var tokens = tokenizer.Encode(prompt);
            using var generatorParams = new GeneratorParams(model);
            
            // Greedy Search constraints: TopP=1, TopK=1
            generatorParams.SetSearchOption("max_length", tokens.SequenceLength + opt.MaxNewTokens);
            generatorParams.SetSearchOption("top_p", 1.0);
            generatorParams.SetSearchOption("top_k", 1);
            generatorParams.SetSearchOption("temperature", 0.0);
            generatorParams.SetInputSequences(tokens);

            using var generator = new Generator(model, generatorParams);
            
            var generatedTokens = new List<int>(opt.MaxNewTokens);
            double totalLogProb = 0.0;
            int generatedCount = 0;

            while (!generator.IsDone() && generatedCount < opt.MaxNewTokens)
            {
                if (cancellationToken.IsCancellationRequested) return null;

                // For low-latency sync-like fast generation, we don't await Task.Yield;
                generator.ComputeLogits();
                generator.GenerateNextToken();
                
                int token = generator.GetSequence(0)[^1];
                generatedTokens.Add(token);
                generatedCount++;
                
                // Compute average log prob for threshold mechanism (conceptually)
                // Since ONNX Runtime GenAI doesn't directly expose token logprobs easily yet in the managed API
                // We're mimicking the gating logic here. In reality, you may need a custom C API interop.
                // For demonstration, we assume valid context for greedy.
            }

            string predictionStr = tokenizer.Decode(generatedTokens.ToArray());

            _lastLatency = sw.GetElapsedTime();

            // Check if prediction is non-meaningful or fails threshold (Mocking log prob threshold check here)
            if (string.IsNullOrWhiteSpace(predictionStr)) return null;

            return new GhostTextResult(predictionStr, false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _modelScope.ReleaseInferenceHandle();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _modelScope.DisposeAsync();
    }
}

file struct ValueStopwatch
{
    private readonly long _startTimestamp;
    
    private ValueStopwatch(long startTimestamp) => _startTimestamp = startTimestamp;

    public static ValueStopwatch StartNew() => new (Stopwatch.GetTimestamp());

    public TimeSpan GetElapsedTime() => Stopwatch.GetElapsedTime(_startTimestamp);
}
