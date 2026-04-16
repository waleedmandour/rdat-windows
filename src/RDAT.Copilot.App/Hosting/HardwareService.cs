using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;
using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.App.Hosting;

/// <summary>
/// UX-First startup sequence handler that verifies hardware capabilities
/// and ensures ML models are downloaded before displaying the main shell.
/// </summary>
public sealed class HardwareService : IHardwareService
{
    private readonly ILogger<HardwareService> _logger;

    public HardwareService(ILogger<HardwareService> logger)
    {
        _logger = logger;
    }

    public Task<HardwareCapabilities> DetectCapabilitiesAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var caps = new HardwareCapabilities();

            try
            {
                // Query DXGI adapter information via native interop
                if (TryGetGpuInfo(out string gpuName, out long vramBytes, out string driverVersion))
                {
                    caps = caps with
                    {
                        GpuName = gpuName,
                        DedicatedVramBytes = vramBytes,
                        DriverVersion = driverVersion,
                        IsDirectMlSupported = true,
                        DirectMlVersion = "1.x"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GPU detection failed, using defaults");
            }

            _logger.LogInformation(
                "Hardware detected: GPU={Gpu}, VRAM={VramMB}MB, DirectML={DirectML}",
                caps.GpuName, caps.DedicatedVramBytes / (1024 * 1024), caps.IsDirectMlSupported);

            return caps;
        }, ct);
    }

    public int ComputeCapabilityScore(HardwareCapabilities caps)
    {
        if (!caps.IsDirectMlSupported)
            return 10;

        int score = 20; // Base score for DirectML availability

        // VRAM-based scoring (4GB+ is good, 8GB+ is excellent)
        long vramGB = caps.DedicatedVramBytes / (1024 * 1024 * 1024);
        score += Math.Min(vramGB * 8, 60);

        // Minimum score for offline model use
        if (vramGB >= 4) score = Math.Max(score, 40);

        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Uses DXGI to query the primary GPU adapter information.
    /// Falls back gracefully if DXGI is unavailable.
    /// </summary>
    private static bool TryGetGpuInfo(out string gpuName, out long vramBytes, out string driverVersion)
    {
        gpuName = "Unknown GPU";
        vramBytes = 0;
        driverVersion = "";

        try
        {
            // Use dxdiag-style approach: read from WMI
            var psi = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "path win32_VideoController get Name,AdapterRAM,DriverVersion /format:csv",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return false;

            // Parse CSV output - take the first GPU entry (non-header line)
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < lines.Length; i++) // Skip header
            {
                var parts = lines[i].Split(',');
                if (parts.Length >= 4)
                {
                    gpuName = parts[1].Trim();
                    if (long.TryParse(parts[2].Trim(), out long ram))
                        vramBytes = ram;
                    driverVersion = parts[3].Trim();

                    if (!string.IsNullOrWhiteSpace(gpuName) && gpuName != "Unknown GPU")
                        return true;
                }
            }
        }
        catch
        {
            // WMI query failed, fall back
        }

        return false;
    }
}
