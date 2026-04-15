using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Services;

namespace RDAT.Copilot.App.Hosting;

/// <summary>
/// UX-First Startup sequence handler that verifies hardware capabilities 
/// and ensures ML models are downloaded strictly before displaying the main shell.
/// </summary>
public sealed class StartupService
{
    private readonly IHardwareInferenceService _hardware;
    private readonly ILlmInferenceService _llmService;
    private readonly ILogger<StartupService> _logger;

    public StartupService(
        IHardwareInferenceService hardware,
        ILlmInferenceService llmService,
        ILogger<StartupService> logger)
    {
        _hardware = hardware;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting RDAT Copilot Initialization Sequence...");

        // 1. Hardware Detection
        var hardwareCap = await _hardware.DetectCapabilitiesAsync(cancellationToken);
        int score = _hardware.ComputeCapabilityScore(hardwareCap);
        
        _logger.LogInformation("Hardware Capability Score: {Score}/100", score);

        // 2. Validate Models Exist (Local Model Validation)
        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string rdatPath = Path.Combine(appDataPath, "RDAT", "Models");
        string llmFolder = Path.Combine(rdatPath, "phi3-mini-4k-instruct-onnx");
        string expectedModelFileName = Path.Combine(llmFolder, "model.onnx");

        if (!File.Exists(expectedModelFileName) && score >= 40)
        {
            // If offline capability exists but no model present, trigger bootstrap UX.
            // Note: In WinUI 3, you would bind this to a modern ContentDialog with a ProgressBar.
            await RunPowershellModelDownloaderAsync(rdatPath);
        }

        // 3. Warm-up Initialization
        if (File.Exists(expectedModelFileName))
        {
            _logger.LogInformation("Warming up local ONNX LLM prior to showing UI...");
            await _llmService.LoadModelAsync(llmFolder, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Insufficient hardware capability or model missing. Falling back to default state.");
        }
    }

    private async Task RunPowershellModelDownloaderAsync(string targetPath)
    {
        _logger.LogInformation("No models found. Attempting to download...");
        
        string psScriptBase = AppDomain.CurrentDomain.BaseDirectory;
        string psScriptPath = Path.Combine(psScriptBase, "download-models.ps1");
        
        if (!File.Exists(psScriptPath))
        {
            // In development it may be in root
            psScriptPath = Path.GetFullPath(Path.Combine(psScriptBase, "..", "..", "..", "..", "download-models.ps1"));
        }

        if (File.Exists(psScriptPath))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{psScriptPath}\" -TargetFolder \"{targetPath}\"",
                UseShellExecute = true, // Shows console for visual feedback out of box, could pipe stdout to WinUI instead
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
            _logger.LogError("Could not find download-models.ps1");
        }
    }
}
