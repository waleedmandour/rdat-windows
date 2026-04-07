using System.Text.Json;

namespace RDAT.Copilot.Desktop.Helpers;

/// <summary>
/// JSON serialization helpers for the WebView2 bridge protocol.
/// Provides consistent camelCase serialization for C# ↔ JS communication.
/// </summary>
public static class JsonBridge
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes an object to camelCase JSON.
    /// </summary>
    public static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// Deserializes camelCase JSON to the specified type.
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    /// <summary>
    /// Creates a bridge message envelope for C# → JS communication.
    /// </summary>
    public static string CreateCommand(string command, object? payload = null)
    {
        return Serialize(new { type = "command", command, payload });
    }

    /// <summary>
    /// Creates a bridge message envelope for JS → C# events.
    /// </summary>
    public static string CreateEvent(string eventType, object? data = null)
    {
        return Serialize(new { type = "event", @event = eventType, data });
    }
}
