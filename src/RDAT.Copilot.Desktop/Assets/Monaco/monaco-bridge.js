// =============================================================================
// RDAT Copilot — Monaco ↔ WebView2 Bridge
// =============================================================================
// This file is loaded by both editor-source.html and editor-target.html.
// It provides the shared communication layer between Monaco Editor and
// the C# WebView2 host via window.chrome.webview.postMessage().
// Phase 2: Added RAG (TM match) ghost text channel with priority ordering.
// =============================================================================

(function () {
  "use strict";

  // ─── Constants ──────────────────────────────────────────────────────
  const DEBOUNCE_MS = 300;
  const GRAMMAR_DEBOUNCE_MS = 2500;
  const BURST_DEBOUNCE_MS = 800;
  const PAUSE_DEBOUNCE_MS = 1200;

  // ─── State ──────────────────────────────────────────────────────────
  let editor = null;
  let monaco = null;
  let paneId = "unknown";
  let isReadOnly = false;
  let debounceTimer = null;
  let grammarDebounceTimer = null;

  // Ghost text state (target only)
  // Phase 2: Three-priority system:
  //   Priority 1: ragSuggestion (GTR — verified TM match, score >= 0.7)
  //   Priority 2: pauseSuggestion (LLM pause channel — 5-20 words)
  //   Priority 3: burstSuggestion (LLM burst channel — 3-5 words)
  let ragSuggestion = null;
  let ragScore = 0;
  let pauseSuggestion = null;
  let burstSuggestion = null;

  // Grammar markers state
  const GRAMMAR_MARKER_OWNER = "rdat-grammar";

  // ─── Post Message to C# ─────────────────────────────────────────────
  function postEvent(eventType, data) {
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage(JSON.stringify({
        type: "event",
        event: eventType,
        data: data || {}
      }));
    }
  }

  // Post a response (for request-response patterns like getText)
  function postResponse(requestId, payload) {
    if (window.chrome?.webview) {
      window.chrome.webview.postMessage(JSON.stringify({
        type: "response",
        requestId: requestId,
        payload: payload || {}
      }));
    }
  }

  // ─── Editor Initialization ──────────────────────────────────────────
  function initEditor(config) {
    paneId = config.paneId || "unknown";
    isReadOnly = config.readOnly || false;

    // Create Monaco editor
    editor = monaco.editor.create(document.getElementById("editor-container"), {
      value: config.initialText || "",
      language: config.languageId || "plaintext",
      theme: "rdat-dark",
      readOnly: isReadOnly,
      fontSize: 15,
      fontFamily: "'Cascadia Code', Consolas, monospace",
      fontLigatures: true,
      lineHeight: 22,
      wordWrap: "on",
      wordWrapColumn: 80,
      minimap: { enabled: false },
      padding: { top: 12, bottom: 12 },
      scrollBeyondLastLine: false,
      smoothScrolling: true,
      renderLineHighlight: isReadOnly ? "none" : "line",
      renderWhitespace: "selection",
      cursorBlinking: "smooth",
      cursorSmoothCaretAnimation: "on",
      bracketPairColorization: { enabled: true },
      lineNumbers: "on",
      glyphMargin: !isReadOnly,
      folding: !isReadOnly,
      lineDecorationsWidth: 8,
      lineNumbersMinChars: 3,
      scrollbar: {
        verticalScrollbarSize: 8,
        horizontalScrollbarSize: 8,
        vertical: "auto",
        horizontal: "auto",
      },
      // Ghost text (target only)
      inlineSuggest: {
        enabled: !isReadOnly,
      },
      quickSuggestions: false,
      suggestOnTriggerCharacters: false,
      tabSize: 2,
      insertSpaces: true,
    });

    // ── Event: Cursor Position Changed ──
    editor.onDidChangeCursorPosition((e) => {
      postEvent("cursorPositionChanged", {
        lineNumber: e.position.lineNumber,
        column: e.position.column,
      });
    });

    // ── Event: Text Changed (debounced) ──
    editor.onDidChangeModelContent(() => {
      if (debounceTimer) clearTimeout(debounceTimer);
      debounceTimer = setTimeout(() => {
        postEvent("textChanged", {
          text: editor.getValue(),
        });
      }, DEBOUNCE_MS);
    });

    // ── Target-only: Inline Completions Provider ──
    if (!isReadOnly) {
      monaco.languages.registerInlineCompletionsProvider(config.languageId || "rdat-target", {
        provideInlineCompletions: (model, position, context, token) => {
          const lineContent = model.getLineContent(position.lineNumber) || "";
          const textBeforeCursor = lineContent.substring(0, position.column - 1);

          // Priority 1: Channel GTR (RAG — verified TM match)
          if (ragSuggestion && ragSuggestion.trim()) {
            const normalizedTyped = textBeforeCursor.replace(/\s+/g, " ").trim();
            const normalizedRag = ragSuggestion.replace(/\s+/g, " ").trim();
            let insertText = normalizedRag;
            if (normalizedTyped && normalizedRag.startsWith(normalizedTyped)) {
              const remainder = normalizedRag.substring(normalizedTyped.length).trim();
              if (remainder) insertText = remainder;
              else return { items: [] };
            }
            return {
              items: [{
                insertText: insertText,
                range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
                filterText: ragSuggestion,
                completionInfo: {
                  providerId: "rdat-gtr",
                  label: `[GTR ${Math.round(ragScore * 100)}%] TM Match`
                }
              }]
            };
          }

          // Priority 2: Channel 6 (Pause — LLM continuation)
          if (pauseSuggestion && pauseSuggestion.trim()) {
            const normalizedTyped = textBeforeCursor.replace(/\s+/g, " ").trim();
            const normalizedPause = pauseSuggestion.replace(/\s+/g, " ").trim();
            let insertText = normalizedPause;
            if (normalizedTyped && normalizedPause.startsWith(normalizedTyped)) {
              const remainder = normalizedPause.substring(normalizedTyped.length).trim();
              if (remainder) insertText = remainder;
              else return { items: [] };
            }
            return {
              items: [{
                insertText: insertText,
                range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
                filterText: pauseSuggestion,
                completionInfo: { providerId: "rdat-pause", label: "[Tab] Complete" }
              }]
            };
          }

          // Priority 3: Channel 5 (Burst — LLM autocomplete)
          if (burstSuggestion && burstSuggestion.trim()) {
            const normalizedTyped = textBeforeCursor.replace(/\s+/g, " ").trim();
            const normalizedBurst = burstSuggestion.replace(/\s+/g, " ").trim();
            let insertText = normalizedBurst;
            if (normalizedTyped && normalizedBurst.startsWith(normalizedTyped)) {
              const remainder = normalizedBurst.substring(normalizedTyped.length).trim();
              if (remainder) insertText = remainder;
              else return { items: [] };
            }
            return {
              items: [{
                insertText: insertText,
                range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
                filterText: burstSuggestion,
                completionInfo: { providerId: "rdat-burst", label: "[Tab] Autocomplete" }
              }]
            };
          }

          return { items: [] };
        },
        freeInlineCompletions: () => {}
      });
    }

    console.log("[RDAT-Bridge] Monaco editor initialized:", paneId, "(readOnly:", isReadOnly, ")");
  }

  // ─── Command Handlers (C# → JS) ──────────────────────────────────────

  const commandHandlers = {
    setText: (payload) => {
      if (!editor) return;
      const model = editor.getModel();
      if (model) {
        editor.setValue(payload.text || "");
        console.log("[RDAT-Bridge] setText:", payload.text?.length, "chars");
      }
    },

    getText: (payload) => {
      if (!editor) return;
      postResponse(payload.requestId, {
        text: editor.getValue()
      });
    },

    triggerInlineSuggest: () => {
      if (!editor) return;
      const action = editor.getAction("editor.action.inlineSuggest.trigger");
      if (action) {
        action.run();
        console.log("[RDAT-Bridge] Inline suggest triggered");
      }
    },

    // Phase 2: RAG ghost text (highest priority)
    setRagSuggestion: (payload) => {
      ragSuggestion = payload.text || null;
      ragScore = payload.score || 0;

      if (ragSuggestion && editor) {
        const action = editor.getAction("editor.action.inlineSuggest.trigger");
        if (action) action.run();
        console.log("[RDAT-Bridge] RAG suggestion set:", ragScore.toFixed(3), "-", ragSuggestion?.substring(0, 40) + "...");
      } else {
        ragSuggestion = null;
        ragScore = 0;
      }
    },

    setPauseSuggestion: (payload) => {
      pauseSuggestion = payload.text || null;
      if (pauseSuggestion && !ragSuggestion && editor) {
        const action = editor.getAction("editor.action.inlineSuggest.trigger");
        if (action) action.run();
      }
    },

    setBurstSuggestion: (payload) => {
      burstSuggestion = payload.text || null;
      if (burstSuggestion && !ragSuggestion && !pauseSuggestion && editor) {
        const action = editor.getAction("editor.action.inlineSuggest.trigger");
        if (action) action.run();
      }
    },

    applyMarkers: (payload) => {
      if (!editor || !monaco) return;
      const model = editor.getModel();
      if (!model) return;

      const markers = (payload.markers || []).map(m => ({
        startLineNumber: m.startLineNumber || 1,
        startColumn: m.startColumn || 1,
        endLineNumber: m.endLineNumber || 1,
        endColumn: m.endColumn || 1,
        message: m.message || "",
        severity: (m.severity === "error" ? 8 : m.severity === "warning" ? 4 : m.severity === "info" ? 2 : 1),
        source: m.source || "Grammar Checker"
      }));

      monaco.editor.setModelMarkers(model, payload.owner || GRAMMAR_MARKER_OWNER, markers);
      console.log("[RDAT-Bridge] Applied", markers.length, "markers");
    },

    clearMarkers: (payload) => {
      if (!editor || !monaco) return;
      const model = editor.getModel();
      if (!model) return;
      monaco.editor.setModelMarkers(model, payload.owner || GRAMMAR_MARKER_OWNER, []);
      console.log("[RDAT-Bridge] Markers cleared");
    },

    highlightLine: (payload) => {
      if (!editor || isReadOnly) return;
      if (payload.lineNumber) {
        // Phase 2: Add line highlight decoration for TM match source
        const line = payload.lineNumber;
        const decorations = editor.deltaDecorations([], [{
          range: new monaco.Range(line, 1, line, 1),
          options: {
            isWholeLine: true,
            className: "tm-match-highlight",
            glyphMarginClassName: "tm-match-glyph",
            glyphMarginHoverMessage: { value: "**TM Match Source**" },
            overviewRuler: {
              color: "#2dd4bf40",
              position: monaco.editor.OverviewRulerLane.Full
            }
          }
        }]);
        // Reveal line
        editor.revealLineInCenter(line);
        console.log("[RDAT-Bridge] TM highlight applied to line:", line);
      }
    }
  };

  // ─── Message Listener (C# → JS) ─────────────────────────────────────
  window.chrome?.webview?.addEventListener("message", (event) => {
    try {
      const message = JSON.parse(event.data);
      if (message.type === "command" && commandHandlers[message.command]) {
        commandHandlers[message.command](message.payload || {});
      }
    } catch (err) {
      console.error("[RDAT-Bridge] Failed to handle message:", err);
    }
  });

  // Also listen via DOM event for WebView2 UWP compatibility
  window.addEventListener("message", (event) => {
    try {
      const message = JSON.parse(event.data);
      if (message.type === "command" && commandHandlers[message.command]) {
        commandHandlers[message.command](message.payload || {});
      }
    } catch (err) {
      // Not a bridge message — ignore
    }
  });

  // ─── Expose init function ───────────────────────────────────────────
  window.initRDATBridge = initEditor;
})();
