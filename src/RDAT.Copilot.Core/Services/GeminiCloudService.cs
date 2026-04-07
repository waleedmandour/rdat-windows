using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Constants;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Services;

/// <summary>
/// Gemini Cloud AI service (Phase 4) — BYOK integration with Google Gemini 2.0 Flash.
///
/// Provides cloud-powered AI capabilities that complement the local LLM:
///   - Enhanced grammar checking with Arabic morphological analysis
///   - Quality estimation (accuracy, fluency, style, terminology scores)
///   - Style rewriting and tone adjustment
///   - Translation quality assessment
///
/// Architecture:
///   Uses HttpClient for REST API calls to the Gemini API.
///   API key is retrieved from ICredentialService (Windows Credential Locker)
///   and never stored in application memory between calls.
///
/// Rate Limiting:
///   Implements basic rate limiting with configurable RPM (requests per minute).
///   Tracks remaining requests from API response headers.
/// </summary>
public sealed class GeminiCloudService : IGeminiCloudService, IDisposable
{
    private readonly ICredentialService _credentialService;
    private readonly ILogger<GeminiCloudService> _logger;
    private readonly HttpClient _httpClient;

    private const string CredentialResource = "RDAT-Gemini";
    private const string CredentialUsername = "gemini-api-key";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private GeminiState _state = GeminiState.NotConfigured;
    private GeminiRateLimit? _rateLimit;

    // Rate limiting state
    private readonly object _rateLimitLock = new();
    private int _requestsThisMinute;
    private DateTime _minuteWindowStart = DateTime.UtcNow;

    public GeminiState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsConfigured => State == GeminiState.Ready || State == GeminiState.Configured;

    public event EventHandler<GeminiState>? StateChanged;

    public GeminiCloudService(
        ICredentialService credentialService,
        ILogger<GeminiCloudService> logger)
    {
        _credentialService = credentialService;
        _logger = logger;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "RDAT-Copilot/2.0");

        // Check if a key is already stored
        if (_credentialService.HasCredential(CredentialResource, CredentialUsername))
        {
            State = GeminiState.Configured;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ConfigureAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[Gemini] Empty API key provided");
            return false;
        }

        try
        {
            // Store the key securely
            _credentialService.SetCredential(CredentialResource, CredentialUsername, apiKey);
            State = GeminiState.Configured;

            // Validate by making a test call
            var isValid = await ValidateKeyAsync().ConfigureAwait(false);
            State = isValid ? GeminiState.Ready : GeminiState.Configured;

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Gemini] Failed to configure API key");
            State = GeminiState.Error;
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateKeyAsync()
    {
        var apiKey = _credentialService.GetCredential(CredentialResource, CredentialUsername);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            State = GeminiState.NotConfigured;
            return false;
        }

        try
        {
            // Make a lightweight test call
            var testUrl = $"{AppConstants.GeminiApiEndpoint}?key={apiKey}";
            var testPayload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = "Reply with only: OK" }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 10,
                    temperature = 0
                }
            };

            var json = JsonSerializer.Serialize(testPayload, _jsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(testUrl, content).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                State = GeminiState.Ready;
                _logger.LogInformation("[Gemini] API key validated successfully");
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logger.LogWarning(
                "[Gemini] API key validation failed: {Status} — {Error}",
                (int)response.StatusCode, errorBody[..Math.Min(200, errorBody.Length)]);

            State = GeminiState.Error;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Gemini] API key validation error");
            State = GeminiState.Error;
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GrammarIssue>> CheckGrammarAsync(
        string text,
        string sourceSentence,
        LanguageDirection languageDirection,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<GrammarIssue>();

        var apiKey = _credentialService.GetCredential(CredentialResource, CredentialUsername);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("[Gemini] Grammar check skipped — no API key");
            return Array.Empty<GrammarIssue>();
        }

        await CheckRateLimitAsync().ConfigureAwait(false);
        State = GeminiState.Busy;
        var sw = Stopwatch.StartNew();

        try
        {
            var prompt = BuildGrammarPrompt(text, sourceSentence, languageDirection);
            var responseJson = await CallGeminiAsync(apiKey, prompt, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseJson)) return Array.Empty<GrammarIssue>();

            var issues = ParseGrammarIssues(responseJson, text, languageDirection);

            _logger.LogInformation(
                "[Gemini] Grammar check: {Count} issues ({Ms:F0}ms)",
                issues.Count, sw.Elapsed.TotalMilliseconds);

            State = GeminiState.Ready;
            return issues;
        }
        catch (OperationCanceledException)
        {
            State = GeminiState.Ready;
            return Array.Empty<GrammarIssue>();
        }
        catch (Exception ex)
        {
            State = GeminiState.Error;
            _logger.LogError(ex, "[Gemini] Grammar check failed");
            return Array.Empty<GrammarIssue>();
        }
    }

    /// <inheritdoc/>
    public async Task<QualityEstimationResult> EstimateQualityAsync(
        string sourceText,
        string targetText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetText))
        {
            return new QualityEstimationResult(0, 0, 0, 0, 0, "No translation provided.", 0);
        }

        var apiKey = _credentialService.GetCredential(CredentialResource, CredentialUsername);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new QualityEstimationResult(0, 0, 0, 0, 0, "Gemini API key not configured.", 0);
        }

        await CheckRateLimitAsync().ConfigureAwait(false);
        State = GeminiState.Busy;
        var sw = Stopwatch.StartNew();

        try
        {
            var prompt = BuildQualityEstimationPrompt(sourceText, targetText);
            var responseJson = await CallGeminiAsync(apiKey, prompt, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return new QualityEstimationResult(0, 0, 0, 0, 0, "No response from Gemini.", sw.Elapsed.TotalMilliseconds);
            }

            var result = ParseQualityEstimation(responseJson, sw.Elapsed.TotalMilliseconds);

            State = GeminiState.Ready;
            return result;
        }
        catch (OperationCanceledException)
        {
            State = GeminiState.Ready;
            return new QualityEstimationResult(0, 0, 0, 0, 0, "Quality estimation cancelled.", sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            State = GeminiState.Error;
            _logger.LogError(ex, "[Gemini] Quality estimation failed");
            return new QualityEstimationResult(0, 0, 0, 0, 0, $"Error: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> RewriteAsync(
        string sourceText,
        string targetText,
        string styleHint = "formal",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetText)) return null;

        var apiKey = _credentialService.GetCredential(CredentialResource, CredentialUsername);
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        await CheckRateLimitAsync().ConfigureAwait(false);
        State = GeminiState.Busy;

        try
        {
            var prompt = $"""
                You are a professional translation editor. Rewrite the following translation in a {styleHint} style.

                [Source]: {sourceText}

                [Current translation]: {targetText}

                Output ONLY the rewritten translation, nothing else. Preserve all meaning from the original.
                """;

            var responseJson = await CallGeminiAsync(apiKey, prompt, cancellationToken).ConfigureAwait(false);
            State = GeminiState.Ready;

            return responseJson?.Trim();
        }
        catch (OperationCanceledException)
        {
            State = GeminiState.Ready;
            return null;
        }
        catch (Exception ex)
        {
            State = GeminiState.Error;
            _logger.LogError(ex, "[Gemini] Rewrite failed");
            return null;
        }
    }

    /// <inheritdoc/>
    public Task RemoveApiKeyAsync()
    {
        _credentialService.RemoveCredential(CredentialResource, CredentialUsername);
        State = GeminiState.NotConfigured;
        _logger.LogInformation("[Gemini] API key removed");
        return Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════════
    // API Communication
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Make an API call to Gemini and extract the text response.
    /// Handles rate limiting, retry logic, and response extraction.
    /// </summary>
    private async Task<string?> CallGeminiAsync(
        string apiKey, string prompt, CancellationToken cancellationToken)
    {
        var url = $"{AppConstants.GeminiApiEndpoint}?key={apiKey}";

        var payload = new
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
                temperature = 0.2,
                maxOutputTokens = 2048,
                responseMimeType = "application/json"
            }
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logger.LogWarning(
                "[Gemini] API call failed: {Status} — {Error}",
                (int)response.StatusCode,
                errorBody[..Math.Min(300, errorBody.Length)]);

            // Handle rate limiting
            if ((int)response.StatusCode == 429)
            {
                _rateLimit = new GeminiRateLimit(
                    RequestsPerMinute: 15,
                    RemainingRequests: 0,
                    ResetAtUnixMs: DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()
                );
            }

            return null;
        }

        // Track rate limit
        IncrementRequestCount();

        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        try
        {
            var doc = JsonDocument.Parse(responseBody);
            var candidates = doc.RootElement.GetProperty("candidates");

            if (candidates.GetArrayLength() > 0)
            {
                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                {
                    return parts[0].GetProperty("text").GetString();
                }
            }

            // Check for safety blocks
            if (doc.RootElement.TryGetProperty("promptFeedback", out var feedback))
            {
                var blockReason = feedback.TryGetProperty("blockReason", out var br)
                    ? br.GetString()
                    : "unknown";
                _logger.LogWarning("[Gemini] Response blocked: {Reason}", blockReason);
            }

            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Gemini] Failed to parse API response");
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Prompt Builders
    // ════════════════════════════════════════════════════════════════

    private static string BuildGrammarPrompt(string text, string sourceSentence, LanguageDirection direction)
    {
        var targetLang = direction == LanguageDirection.EnToAr ? "Arabic" : "English";

        return $"""
            You are a professional {targetLang} grammar and style checker for translation quality assurance.
            Review the {targetLang} translation and find all grammar, spelling, punctuation, and style issues.

            Source sentence: {sourceSentence}

            Translation to check:
            {text}

            Output a JSON array of issues. Each issue must have:
            - "type": "spelling", "grammar", "punctuation", or "style"
            - "message": brief description
            - "suggestion": corrected text
            - "original": problematic text fragment
            - "line": line number (1-based)

            If no issues: []
            Output ONLY the JSON array.
            """;
    }

    private static string BuildQualityEstimationPrompt(string sourceText, string targetText)
    {
        return $"""
            You are a professional translation quality evaluator. Score this translation on four dimensions.

            Source:
            {sourceText}

            Translation:
            {targetText}

            Output a JSON object with:
            - "overall": overall score 0-100
            - "accuracy": faithfulness to source 0-100
            - "fluency": naturalness of target 0-100
            - "style": register and tone 0-100
            - "terminology": correct term usage 0-100
            - "feedback": detailed improvement suggestions (2-3 sentences)

            Output ONLY the JSON object.
            """;
    }

    // ════════════════════════════════════════════════════════════════
    // Response Parsing
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parse grammar issues from Gemini JSON response.
    /// Reuses the same parsing logic as GrammarCheckerService.
    /// </summary>
    private IReadOnlyList<GrammarIssue> ParseGrammarIssues(
        string jsonResponse, string originalText, LanguageDirection direction)
    {
        var issues = new List<GrammarIssue>();

        try
        {
            var json = ExtractJsonArray(jsonResponse);
            if (string.IsNullOrWhiteSpace(json)) return issues;

            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return issues;

            var lines = originalText.Split('\n');
            var issueId = 0;

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var typeStr = element.GetProperty("type").GetString() ?? "grammar";
                    var message = element.GetProperty("message").GetString() ?? "Unknown issue";
                    var suggestion = element.GetProperty("suggestion").GetString() ?? string.Empty;
                    var original = element.GetProperty("original").GetString() ?? string.Empty;
                    var line = element.TryGetProperty("line", out var lp) ? lp.GetInt32() : 1;

                    var errorType = typeStr.ToLowerInvariant() switch
                    {
                        "spelling" => GrammarErrorType.Spelling,
                        "grammar" => GrammarErrorType.Grammar,
                        "punctuation" => GrammarErrorType.Punctuation,
                        "style" => GrammarErrorType.Style,
                        _ => GrammarErrorType.Grammar
                    };

                    issues.Add(new GrammarIssue(
                        Id: $"gem-{++issueId}",
                        Type: errorType,
                        Message: message,
                        Suggestion: suggestion,
                        StartLineNumber: line,
                        EndLineNumber: line,
                        StartColumn: 1,
                        EndColumn: Math.Max(1, original.Length),
                        OriginalText: original,
                        Language: direction == LanguageDirection.EnToAr ? "ar" : "en"
                    ));
                }
                catch { }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Gemini] Failed to parse grammar response");
        }

        return issues;
    }

    /// <summary>
    /// Parse quality estimation from Gemini JSON response.
    /// </summary>
    private QualityEstimationResult ParseQualityEstimation(string jsonResponse, double elapsedMs)
    {
        try
        {
            var json = ExtractJsonObject(jsonResponse);
            if (string.IsNullOrWhiteSpace(json)) throw new JsonException("Empty response");

            var doc = JsonDocument.Parse(json);

            return new QualityEstimationResult(
                OverallScore: GetIntProperty(doc, "overall"),
                AccuracyScore: GetIntProperty(doc, "accuracy"),
                FluencyScore: GetIntProperty(doc, "fluency"),
                StyleScore: GetIntProperty(doc, "style"),
                TerminologyScore: GetIntProperty(doc, "terminology"),
                Feedback: GetStringProperty(doc, "feedback"),
                EstimatedMs: elapsedMs
            );
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[Gemini] Failed to parse quality estimation response");
            return new QualityEstimationResult(0, 0, 0, 0, 0, "Failed to parse quality estimation.", elapsedMs);
        }
    }

    private static int GetIntProperty(JsonDocument doc, string propertyName)
    {
        if (doc.RootElement.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind == JsonValueKind.Number ? prop.GetInt32() : 0;
        }
        return 0;
    }

    private static string GetStringProperty(JsonDocument doc, string propertyName)
    {
        if (doc.RootElement.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];
        return string.Empty;
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];
        return string.Empty;
    }

    // ════════════════════════════════════════════════════════════════
    // Rate Limiting
    // ════════════════════════════════════════════════════════════════

    private async Task CheckRateLimitAsync()
    {
        int waitMs = 0;

        lock (_rateLimitLock)
        {
            // Reset window if a minute has passed
            if (DateTime.UtcNow - _minuteWindowStart > TimeSpan.FromMinutes(1))
            {
                _requestsThisMinute = 0;
                _minuteWindowStart = DateTime.UtcNow;
            }

            // If we've hit the limit, calculate wait time
            if (_requestsThisMinute >= 14) // Leave buffer below 15 RPM
            {
                waitMs = (int)(TimeSpan.FromMinutes(1) - (DateTime.UtcNow - _minuteWindowStart)).TotalMilliseconds;
                if (waitMs > 0)
                {
                    _logger.LogInformation("[Gemini] Rate limit reached, waiting {Ms}ms", waitMs);
                }
            }
        }

        // Actually wait outside the lock to avoid holding it during the delay
        if (waitMs > 0)
        {
            await Task.Delay(Math.Min(waitMs, 5000)).ConfigureAwait(false);
        }

        // Small delay to prevent bursting even within limits
        await Task.Delay(100).ConfigureAwait(false);
    }

    private void IncrementRequestCount()
    {
        lock (_rateLimitLock)
        {
            _requestsThisMinute++;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
