// ========================================================================
// RDAT Copilot - Hardware Service Interface
// Location: src/RDAT.Copilot.Core/Interfaces/
// ========================================================================

using RDAT.Copilot.Core.Models;

namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Detects local GPU/VRAM capabilities for inference backend selection.
/// Used during startup to determine DirectML availability and capacity.
/// </summary>
public interface IHardwareService
{
    /// <summary>Detects GPU, VRAM, and DirectML support on the local machine.</summary>
    Task<HardwareCapabilities> DetectCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>Computes a 0-100 capability score from the detected hardware.</summary>
    int ComputeCapabilityScore(HardwareCapabilities caps);
}
