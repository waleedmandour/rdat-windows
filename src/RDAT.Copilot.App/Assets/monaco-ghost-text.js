// monaco-ghost-text.js
// Responsible for capturing keystrokes, posting them to WebView2 C# host,
// and rendering Ghost Text predictions using line-end decorations for LTR/RTL correctly.

window.initGhostTextFeatures = function(editor) {
    let currentDecorationIds = [];
    let isRtl = false;
    let currentGhostText = '';

    // Style injection for GhostText
    const style = document.createElement('style');
    style.innerHTML = `
        .ghost-text-ltr {
            opacity: 0.5;
            font-style: italic;
            color: #6e7681;
            padding-left: 2px;
        }
        .ghost-text-rtl {
            opacity: 0.5;
            font-style: italic;
            color: #6e7681;
            padding-right: 2px;
            direction: rtl;
            unicode-bidi: embed;
        }
    `;
    document.head.appendChild(style);

    // Listen for content changes to send keystroke events to C# backend
    let debounceTimer = null;
    editor.onDidChangeModelContent((event) => {
        if (event.isFlush) return;

        // Debounce keystroke events (300ms) to avoid overwhelming the inference pipeline
        if (debounceTimer) clearTimeout(debounceTimer);
        debounceTimer = setTimeout(function() {
            const position = editor.getPosition();
            const model = editor.getModel();

            if (!position || !model) return;

            const targetText = model.getValueInRange({
                startLineNumber: position.lineNumber,
                startColumn: 1,
                endLineNumber: position.lineNumber,
                endColumn: position.column
            });

            // Get the full source text from the opposite editor (if available)
            const sourceText = model.getValue();

            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({
                    type: "keystroke",
                    source: sourceText.substring(0, 500), // Limit source text length
                    target: targetText,
                    lang: isRtl ? "en-ar" : "ar-en"
                });
            }
        }, 300);
    });

    // Listen for messages from C# backend
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            const data = event.data;
            if (data.type === 'ghostTextResult') {
                renderGhostText(data.text);
            } else if (data.type === 'setDirection') {
                isRtl = data.isRtl;
                editor.updateOptions({
                    extraEditorClassName: isRtl ? 'rtl-editor' : ''
                });
            }
        });
    }

    function renderGhostText(suggestion) {
        const position = editor.getPosition();
        if (!position) return;

        if (!suggestion || suggestion.trim().length === 0) {
            currentDecorationIds = editor.deltaDecorations(currentDecorationIds, []);
            currentGhostText = '';
            return;
        }

        currentGhostText = suggestion;
        const decorationClass = isRtl ? 'ghost-text-rtl' : 'ghost-text-ltr';

        const ghostTextDecoration = {
            range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
            options: {
                isWholeLine: false,
                after: {
                    content: suggestion,
                    inlineClassName: decorationClass
                }
            }
        };

        currentDecorationIds = editor.deltaDecorations(currentDecorationIds, [ghostTextDecoration]);
    }

    // Clear ghost text on cursor selection change
    editor.onDidChangeCursorSelection(() => {
        // Don't clear if user is just moving through the ghost text
        currentDecorationIds = editor.deltaDecorations(currentDecorationIds, []);
    });

    // Accept ghost text with Tab key
    editor.addCommand(monaco.KeyMod.Tab, () => {
        if (currentGhostText && currentGhostText.length > 0) {
            const position = editor.getPosition();
            if (!position) return;

            // Clear the ghost text decoration first
            currentDecorationIds = editor.deltaDecorations(currentDecorationIds, []);

            // Insert the ghost text at cursor position
            editor.executeEdits("ghost-text-accept", [
                {
                    range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
                    text: currentGhostText,
                    forceMoveMarkers: true
                }
            ]);

            currentGhostText = '';
        } else {
            // Default Tab behavior: insert tab character
            editor.trigger('keyboard', 'type', { text: '\t' });
        }
    });

    // Reject ghost text with Escape key
    editor.addCommand(monaco.KeyCode.Escape, () => {
        if (currentGhostText && currentGhostText.length > 0) {
            currentDecorationIds = editor.deltaDecorations(currentDecorationIds, []);
            currentGhostText = '';
        }
    });

    // Expose for external access
    window.rdatGetGhostText = function() { return currentGhostText; };
    window.rdatSetDirection = function(rtl) { isRtl = rtl; };
};
