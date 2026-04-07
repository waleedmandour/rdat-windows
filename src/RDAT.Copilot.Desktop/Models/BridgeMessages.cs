namespace RDAT.Copilot.Desktop.Models;

/// <summary>
/// C# records that mirror the JavaScript bridge message types.
/// Used for type-safe deserialization of WebView2 messages.
/// </summary>

/// <summary>
/// Base bridge message with type discriminator.
/// </summary>
public record BridgeMessage(string Type);

/// <summary>
/// Command message (C# → JS).
/// </summary>
public record BridgeCommand(string Type, string Command, object? Payload = null) : BridgeMessage(Type);

/// <summary>
/// Event message (JS → C#).
/// </summary>
public record BridgeEvent(string Type, string Event, object? Data = null) : BridgeMessage(Type);

/// <summary>
/// Cursor position changed event from Monaco.
/// </summary>
public record CursorPositionChangedEvent(int LineNumber, int Column);

/// <summary>
/// Text changed event from Monaco.
/// </summary>
public record TextChangedEvent(string Text);

/// <summary>
/// Grammar marker for Monaco editor.
/// </summary>
public record GrammarMarker(
    int StartLineNumber,
    int StartColumn,
    int EndLineNumber,
    int EndColumn,
    string Message,
    string Severity,
    string Source = "Grammar Checker"
);

/// <summary>
/// Ghost text suggestion from the LLM.
/// </summary>
public record GhostTextSuggestion(
    string Channel,     // "pause", "burst", "predictive"
    string InsertText,
    int StartLine,
    int StartColumn,
    string ProviderId,
    string Label
);
