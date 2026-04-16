using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.App.Hosting;

/// <summary>
/// UX-First Startup sequence handler that verifies hardware capabilities
/// and ensures ML models are downloaded strictly before displaying the main shell.
/// </summary>
public sealed class StartupService
{
    private readonly IHardwareService _hardwareService;
    private readonly ILlmInferenceService _llmService;
    private readonly ILogger<StartupService> _logger;

    public StartupService(
        IHardwareService hardwareService,
        ILlmInferenceService llmService,
        ILogger<StartupService> logger)
    {
        _hardwareService = hardwareService ?? throw new ArgumentNullException(nameof(hardwareService));
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting RDAT Copilot Initialization Sequence...");

        // Phase 1: Hardware Detection
        var hardwareCap = await _hardwareService.DetectCapabilitiesAsync(cancellationToken);
        int score = _hardwareService.ComputeCapabilityScore(hardwareCap);

        _logger.LogInformation("Hardware Capability Score: {Score}/100", score);

        // Phase 2: Validate Models Exist
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string rdatPath = Path.Combine(appDataPath, "RDAT", "Models");
        string llmFolder = Path.Combine(rdatPath, "phi3-mini-4k-instruct-onnx");
        string expectedModelFileName = Path.Combine(llmFolder, "model.onnx");

        if (!File.Exists(expectedModelFileName) && score >= 40)
        {
            _logger.LogInformation("No local model found. Downloading models...");
            await RunPowershellModelDownloaderAsync(rdatPath);
        }

        // Phase 3: Warm-up Initialization
        if (File.Exists(expectedModelFileName))
        {
            _logger.LogInformation("Warming up local ONNX LLM prior to showing UI...");
            try
            {
                await _llmService.LoadModelAsync(llmFolder, cancellationToken);
                _logger.LogInformation("ONNX LLM warm-up complete. Inference mode: {Mode}", _llmService.InferenceMode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to warm up ONNX LLM model. Proceeding without local inference.");
            }
        }
        else
        {
            _logger.LogWarning("Insufficient hardware or model missing. Starting in limited mode.");
        }
    }

    private async Task RunPowershellModelDownloaderAsync(string targetPath)
    {
        string psScriptBase = AppDomain.CurrentDomain.BaseDirectory;
        string psScriptPath = Path.Combine(psScriptBase, "download-models.ps1");

        if (!File.Exists(psScriptPath))
        {
            psScriptPath = Path.GetFullPath(Path.Combine(psScriptBase, "..", "..", "..", "..", "download-models.ps1"));
        }

        if (File.Exists(psScriptPath))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{psScriptPath}\" -TargetFolder \"{targetPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        else
        {
            _logger.LogWarning("Model download script not found at expected locations.");
        }
    }
}
