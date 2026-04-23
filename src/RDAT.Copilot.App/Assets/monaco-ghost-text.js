// monaco-ghost-text.js 
// Responsible for capturing keystrokes, posting them to WebView2 C# host,
// and rendering Ghost Text predictions using line-end decorations for LTR/RTL correctly.

window.initGhostTextFeatures = function(editor) {
    let currentDecorationIds = [];
    let isRtl = false;

    // Optional style injection for GhostText
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

    editor.onDidChangeModelContent((event) => {
        if (event.isFlush) return;

        const position = editor.getPosition();
        const model = editor.getModel();
        
        const targetText = model.getValueInRange({
            startLineNumber: position.lineNumber,
            startColumn: 1,
            endLineNumber: position.lineNumber,
            endColumn: position.column
        });
        
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({
                type: "keystroke",
                source: "Simulated Source Segment", 
                target: targetText,
                lang: isRtl ? "en-ar" : "ar-en" // Dynamic directionality
            });
        }
    });

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            const data = event.data;
            if (data.type === 'ghostTextResult') {
                renderGhostText(data.text);
            } else if (data.type === 'setDirection') {
                // Allows C# to toggle RTL
                isRtl = data.isRtl;
                editor.updateOptions({
                    extraEditorClassName: isRtl ? 'rtl-editor' : ''
                });
            }
        });
    }

    function renderGhostText(suggestion) {
        const position = editor.getPosition();
        
        if (!suggestion || suggestion.trim().length === 0) {
            currentDecorationIds = editor.deltaDecorations(currentDecorationIds, []);
            return;
        }

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

    editor.onDidChangeCursorSelection(() => {
        currentDecorationIds = editor.deltaDecorations(currentDecorationIds, []);
    });

    editor.addCommand(monaco.KeyCode.Tab, () => {
        if (currentDecorationIds.length > 0) {
            const dec = editor.getModel().getDecorationOptions(currentDecorationIds[0]);
            if (dec && dec.after && dec.after.content) {
                const suggestion = dec.after.content;
                currentDecorationIds = editor.deltaDecorations(currentDecorationIds, []); 
                
                const position = editor.getPosition();
                editor.executeEdits("ghost-text", [
                    {
                        range: new monaco.Range(position.lineNumber, position.column, position.lineNumber, position.column),
                        text: suggestion,
                        forceMoveMarkers: true
                    }
                ]);
            }
        }
    }, 'when editorHasGhostText');
};
