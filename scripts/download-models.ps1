<#
.SYNOPSIS
Downloads necessary ONNX models for RDAT Copilot including LLM and Embedding models.
.DESCRIPTION
This script fetches the MiniLM-L6-v2 ONNX embedding model and a tiny dummy/testing LLM model for the ONNX GenAI testing.
#>

param (
    [string]$TargetFolder = ".\Models"
)

# 1. Ensure local model directories
$EmbeddingFolder = Join-Path $TargetFolder "minilm-l6-v2"
$LlmFolder = Join-Path $TargetFolder "phi3-mini-4k-instruct-onnx"

New-Item -ItemType Directory -Force -Path $EmbeddingFolder | Out-Null
New-Item -ItemType Directory -Force -Path $LlmFolder | Out-Null

# 2. Download Embedding Model
# We'll download a commonly used ONNX version of all-MiniLM-L6-v2 from HuggingFace
$embeddingFiles = @{
    "model.onnx" = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    "tokenizer.json" = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/tokenizer.json";
    "tokenizer_config.json" = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/tokenizer_config.json"
}

Write-Host "Downloading Embedding model (MiniLM-L6) into $EmbeddingFolder..."
foreach ($key in $embeddingFiles.Keys) {
    $url = $embeddingFiles[$key]
    $dest = Join-Path $EmbeddingFolder $key
    if (-not (Test-Path $dest)) {
        Write-Host "  -> Downloading $key"
        Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
    } else {
        Write-Host "  -> $key already exists. Skipping."
    }
}

# 3. Download Generative LLM
# Note: Phi-3-mini is popular. Here we specify just placeholders for an actual model repo.
# Microsoft currently publishes DirectML int4 packages for ONNX GenAI.
$llmFiles = @{
    "genai_config.json" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/genai_config.json";
    "model.onnx" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/model.onnx";
    "model.onnx.data" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/model.onnx.data";
    "tokenizer.json" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/tokenizer.json";
    "tokenizer_config.json" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/tokenizer_config.json";
    "tokenizer.model" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/tokenizer.model";
    "added_tokens.json" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/added_tokens.json";
    "special_tokens_map.json" = "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/main/directml/directml-int4-awq-block-128/special_tokens_map.json"
}

Write-Host "Downloading LLM (Phi-3-mini 4K DirectML INT4) into $LlmFolder..."
Write-Host "NOTE: This will download several GBs!"
foreach ($key in $llmFiles.Keys) {
    $url = $llmFiles[$key]
    $dest = Join-Path $LlmFolder $key
    if (-not (Test-Path $dest)) {
        Write-Host "  -> Downloading $key"
        try {
            Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
        } catch {
            Write-Host "     Failed to download $key. (Some files might be large and require git-lfs or HuggingFace Auth). URL: $url" -ForegroundColor Red
        }
    } else {
        Write-Host "  -> $key already exists. Skipping."
    }
}

Write-Host "Complete!" -ForegroundColor Green
