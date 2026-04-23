<#
.SYNOPSIS
Downloads necessary ONNX models for RDAT Copilot including LLM and Embedding models.
.DESCRIPTION
This script fetches the MiniLM-L6-v2 ONNX embedding model and the Phi-3-mini-4k-instruct
DirectML INT4 model for the ONNX GenAI ghost text pipeline.
#>

param (
    [string]$TargetFolder = ".\Models"
)

# 1. Ensure local model directories
$EmbeddingFolder = Join-Path $TargetFolder "minilm-l6-v2"
$LlmFolder = Join-Path $TargetFolder "phi3-mini-4k-instruct-onnx"

New-Item -ItemType Directory -Force -Path $EmbeddingFolder | Out-Null
New-Item -ItemType Directory -Force -Path $LlmFolder | Out-Null

# 2. Download Embedding Model (all-MiniLM-L6-v2 from Xenova/HuggingFace)
$embeddingFiles = @{
    "model.onnx" = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    "tokenizer.json" = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/tokenizer.json";
    "tokenizer_config.json" = "https://huggingface.co/Xenova/all-MiniLM-L6-v2/resolve/main/tokenizer_config.json"
}

Write-Host "Downloading Embedding model (MiniLM-L6) into $EmbeddingFolder..." -ForegroundColor Cyan
foreach ($key in $embeddingFiles.Keys) {
    $url = $embeddingFiles[$key]
    $dest = Join-Path $EmbeddingFolder $key
    if (-not (Test-Path $dest)) {
        Write-Host "  -> Downloading $key" -ForegroundColor Yellow
        try {
            Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
            Write-Host "  -> $key downloaded successfully." -ForegroundColor Green
        } catch {
            Write-Host "  -> Failed to download $key. URL: $url" -ForegroundColor Red
        }
    } else {
        Write-Host "  -> $key already exists. Skipping." -ForegroundColor Gray
    }
}

# 3. Download Generative LLM (Phi-3-mini-4k-instruct DirectML INT4)
# Microsoft publishes DirectML INT4 packages for ONNX GenAI on HuggingFace.
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

Write-Host ""
Write-Host "Downloading LLM (Phi-3-mini 4K DirectML INT4) into $LlmFolder..." -ForegroundColor Cyan
Write-Host "NOTE: This will download several GBs! Please be patient." -ForegroundColor Yellow
foreach ($key in $llmFiles.Keys) {
    $url = $llmFiles[$key]
    $dest = Join-Path $LlmFolder $key
    if (-not (Test-Path $dest)) {
        Write-Host "  -> Downloading $key" -ForegroundColor Yellow
        try {
            Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
            Write-Host "  -> $key downloaded successfully." -ForegroundColor Green
        } catch {
            Write-Host "  -> Failed to download $key. (Large files may require git-lfs or HuggingFace Auth). URL: $url" -ForegroundColor Red
        }
    } else {
        Write-Host "  -> $key already exists. Skipping." -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Model Download Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Embedding: $EmbeddingFolder" -ForegroundColor White
Write-Host "  LLM:       $LlmFolder" -ForegroundColor White
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor Gray
Write-Host "  1. Build: dotnet build RDAT.Copilot.sln -c Release -p:Platform=x64" -ForegroundColor Gray
Write-Host "  2. Publish: .\scripts\Build-RDAT.ps1 -Clean" -ForegroundColor Gray
Write-Host "  3. Or configure Gemini 3.0 Flash API key for cloud fallback" -ForegroundColor Gray
