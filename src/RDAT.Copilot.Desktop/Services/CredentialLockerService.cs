using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using RDAT.Copilot.Core.Interfaces;

namespace RDAT.Copilot.Desktop.Services;

/// <summary>
/// Windows Credential Locker implementation of ICredentialService (Phase 4).
/// Uses the Windows.Security.Credentials.PasswordVault API to securely store
/// and retrieve API keys and other sensitive credentials.
///
/// On non-Windows platforms, falls back to a local encrypted file in %LOCALAPPDATA%.
///
/// Usage:
///   var credentialService = new CredentialLockerService(logger);
///   credentialService.SetCredential("RDAT-Gemini", "gemini-api-key", "AIza...");
///   var key = credentialService.GetCredential("RDAT-Gemini", "gemini-api-key");
/// </summary>
public class CredentialLockerService : ICredentialService
{
    private readonly ILogger<CredentialLockerService> _logger;
    private readonly string _fallbackPath;

    public CredentialLockerService(ILogger<CredentialLockerService> logger)
    {
        _logger = logger;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _fallbackPath = Path.Combine(localAppData, "RDAT", "credentials.enc");
    }

    /// <inheritdoc/>
    public bool SetCredential(string resource, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(resource) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("[Credential] SetCredential called with empty resource or password");
            return false;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return SetCredentialWindows(resource, username, password);
            }

            return SetCredentialFallback(resource, username, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Credential] Failed to set credential for {Resource}", resource);
            return false;
        }
    }

    /// <inheritdoc/>
    public string? GetCredential(string resource, string username)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            return null;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetCredentialWindows(resource, username);
            }

            return GetCredentialFallback(resource, username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Credential] Failed to get credential for {Resource}", resource);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool RemoveCredential(string resource, string username)
    {
        if (string.IsNullOrWhiteSpace(resource)) return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return RemoveCredentialWindows(resource, username);
            }

            return RemoveCredentialFallback(resource, username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Credential] Failed to remove credential for {Resource}", resource);
            return false;
        }
    }

    /// <inheritdoc/>
    public bool HasCredential(string resource, string username)
    {
        return !string.IsNullOrWhiteSpace(GetCredential(resource, username));
    }

    // ════════════════════════════════════════════════════════════════
    // Windows Credential Locker (PasswordVault)
    // ════════════════════════════════════════════════════════════════

    private bool SetCredentialWindows(string resource, string username, string password)
    {
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            vault.Add(new Windows.Security.Credentials.PasswordCredential(
                resource, username ?? resource, password));

            _logger.LogDebug("[Credential] Stored in PasswordVault: {Resource}", resource);
            return true;
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070005)) // E_ACCESSDENIED
        {
            _logger.LogWarning("[Credential] Access denied to PasswordVault — trying update");
            try
            {
                var vault = new Windows.Security.Credentials.PasswordVault();
                // Retrieve existing to replace
                var existing = vault.Retrieve(resource, username ?? resource);
                existing.Password = password;
                vault.Remove(existing);
                vault.Add(existing);

                _logger.LogDebug("[Credential] Updated in PasswordVault: {Resource}", resource);
                return true;
            }
            catch
            {
                // Fall through to fallback
                return SetCredentialFallback(resource, username, password);
            }
        }
    }

    private string? GetCredentialWindows(string resource, string username)
    {
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            var credential = vault.Retrieve(resource, username ?? resource);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490)) // E_NOTFOUND
        {
            _logger.LogDebug("[Credential] Not found in PasswordVault: {Resource}", resource);
            return null;
        }
    }

    private bool RemoveCredentialWindows(string resource, string username)
    {
        try
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            var credential = vault.Retrieve(resource, username ?? resource);
            vault.Remove(credential);
            _logger.LogDebug("[Credential] Removed from PasswordVault: {Resource}", resource);
            return true;
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490))
        {
            return false; // Not found — already removed
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Fallback: DPAPI-encrypted local file
    // ════════════════════════════════════════════════════════════════

    private bool SetCredentialFallback(string resource, string username, string password)
    {
        try
        {
            var dir = Path.GetDirectoryName(_fallbackPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var credentials = ReadFallbackCredentials();
            credentials[resource] = System.Text.Json.JsonSerializer.Serialize(new
            {
                username = username ?? resource,
                password = Convert.ToBase64String(
                    System.Security.Cryptography.ProtectedData.Protect(
                        System.Text.Encoding.UTF8.GetBytes(password),
                        System.Text.Encoding.UTF8.GetBytes(resource),
                        System.Security.Cryptography.DataProtectionScope.CurrentUser))
            });

            var json = System.Text.Json.JsonSerializer.Serialize(credentials, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false
            });
            File.WriteAllText(_fallbackPath, json);

            _logger.LogDebug("[Credential] Stored in fallback file: {Resource}", resource);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Credential] Fallback storage failed for {Resource}", resource);
            return false;
        }
    }

    private string? GetCredentialFallback(string resource, string username)
    {
        try
        {
            var credentials = ReadFallbackCredentials();
            if (!credentials.TryGetValue(resource, out var value)) return null;

            var entry = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value);
            var encryptedPassword = entry.GetProperty("password").GetString();
            if (string.IsNullOrWhiteSpace(encryptedPassword)) return null;

            var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(encryptedPassword),
                System.Text.Encoding.UTF8.GetBytes(resource),
                System.Security.Cryptography.DataProtectionScope.CurrentUser);

            return System.Text.Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Credential] Fallback retrieval failed for {Resource}", resource);
            return null;
        }
    }

    private bool RemoveCredentialFallback(string resource, string username)
    {
        try
        {
            var credentials = ReadFallbackCredentials();
            if (credentials.Remove(resource))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(credentials, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = false
                });
                File.WriteAllText(_fallbackPath, json);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Dictionary<string, string> ReadFallbackCredentials()
    {
        try
        {
            if (File.Exists(_fallbackPath))
            {
                var json = File.ReadAllText(_fallbackPath);
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
        }
        catch { }

        return new Dictionary<string, string>();
    }
}
