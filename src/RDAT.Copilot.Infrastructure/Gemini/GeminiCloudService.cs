using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Infrastructure.Gemini;

/// <summary>
/// Google Gemini API cloud fallback for translation when local ONNX
/// inference is unavailable. Uses the Gemini 1.5 Flash model for
/// low-latency ghost text predictions and full translations.
///
/// Privacy note: Only used when the user explicitly configures an API key.
/// By default, the app is 100% offline.
/// </summary>
public sealed class GeminiCloudService : IGeminiService
{
    private readonly ILogger<GeminiCloudService> _logger;
    private readonly HttpClient _httpClient;
    private string? _apiKey;

    private const string GeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public GeminiCloudService(ILogger<GeminiCloudService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public void Configure(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty", nameof(apiKey));

        _apiKey = apiKey;
        _logger.LogInformation("Gemini API key configured");
    }

    public async Task<GhostTextResult> GetPredictionAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return new GhostTextResult { Text = "", Confidence = 0, Source = "cloud" };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var prompt = BuildGhostTextPrompt(sourceText, sourceLang, targetLang);
            var response = await CallGeminiAsync(prompt, 128, cancellationToken);
            sw.Stop();

            return new GhostTextResult
            {
                Text = response,
                Confidence = 0.9,
                LatencyMs = (long)sw.Elapsed.TotalMilliseconds,
                Source = "cloud",
                IsSuppressed = false
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Gemini prediction failed after {Ms}ms", sw.ElapsedMilliseconds);
            return new GhostTextResult { Text = "", Confidence = 0, LatencyMs = (long)sw.Elapsed.TotalMilliseconds, Source = "cloud" };
        }
    }

    public async Task<GhostTextResult> GetFullTranslationAsync(
        string sourceText,
        string sourceLang = "en",
        string targetLang = "ar",
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return new GhostTextResult { Text = "", Confidence = 0, Source = "cloud" };

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var prompt = BuildFullTranslationPrompt(sourceText, sourceLang, targetLang);
            var response = await CallGeminiAsync(prompt, 512, cancellationToken);
            sw.Stop();

            return new GhostTextResult
            {
                Text = response,
                Confidence = 0.95,
                LatencyMs = (long)sw.Elapsed.TotalMilliseconds,
                Source = "cloud",
                IsSuppressed = false
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Gemini full translation failed after {Ms}ms", sw.ElapsedMilliseconds);
            return new GhostTextResult { Text = "", Confidence = 0, LatencyMs = (long)sw.Elapsed.TotalMilliseconds, Source = "cloud" };
        }
    }

    private async Task<string> CallGeminiAsync(string prompt, int maxTokens, CancellationToken ct)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = maxTokens,
                temperature = 0.3,
                topP = 0.9
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var url = $"{GeminiEndpoint}?key={_apiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);

        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return text.Trim();
    }

    private static string BuildGhostTextPrompt(string sourceText, string sourceLang, string targetLang)
    {
        return $"You are a professional {sourceLang}-to-{targetLang} translator. " +
               $"Output ONLY the {targetLang} translation text, nothing else. No explanations, no notes.\n\n" +
               $"Translate this text to {targetLang}:\n\n{sourceText}\n\n" +
               $"Provide a concise translation (3-5 words max for ghost text prediction):";
    }

    private static string BuildFullTranslationPrompt(string sourceText, string sourceLang, string targetLang)
    {
        return $"You are a professional {sourceLang}-to-{targetLang} translator. " +
               $"Output ONLY the {targetLang} translation, nothing else. No explanations, no notes.\n\n" +
               $"Translate this text to {targetLang}:\n\n{sourceText}";
    }
}
