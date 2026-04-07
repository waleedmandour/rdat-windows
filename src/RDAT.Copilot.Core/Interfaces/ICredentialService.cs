namespace RDAT.Copilot.Core.Interfaces;

/// <summary>
/// Contract for secure credential storage service (Phase 4).
/// Abstracts platform-specific secure storage (Windows Credential Locker,
/// macOS Keychain, Linux Secret Service) for API key management.
/// The Desktop layer provides the Windows implementation using PasswordVault.
/// </summary>
public interface ICredentialService
{
    /// <summary>
    /// Store a credential securely.
    /// </summary>
    /// <param name="resource">Resource name (e.g., "RDAT-Gemini").</param>
    /// <param name="username">Username or identifier (e.g., "gemini-api-key").</param>
    /// <param name="password">The secret value (API key).</param>
    /// <returns>True if stored successfully.</returns>
    bool SetCredential(string resource, string username, string password);

    /// <summary>
    /// Retrieve a stored credential.
    /// </summary>
    /// <param name="resource">Resource name.</param>
    /// <param name="username">Username or identifier.</param>
    /// <returns>The secret value, or null if not found.</returns>
    string? GetCredential(string resource, string username);

    /// <summary>
    /// Remove a stored credential.
    /// </summary>
    /// <param name="resource">Resource name.</param>
    /// <param name="username">Username or identifier.</param>
    /// <returns>True if removed successfully.</returns>
    bool RemoveCredential(string resource, string username);

    /// <summary>
    /// Check if a credential exists for the given resource/username.
    /// </summary>
    /// <param name="resource">Resource name.</param>
    /// <param name="username">Username or identifier.</param>
    /// <returns>True if the credential exists.</returns>
    bool HasCredential(string resource, string username);
}
