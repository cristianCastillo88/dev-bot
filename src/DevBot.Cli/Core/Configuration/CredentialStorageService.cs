using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;

namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Servicio que gestiona la persistencia segura de credenciales locales en ~/.devbot/api_key
/// utilizando Data Protection API para cifrado en reposo y permisos ACL restrictivos.
/// </summary>
public sealed class CredentialStorageService : ICredentialStorageService
{
    private readonly string _storageDir;
    private readonly string _apiKeyFilePath;
    private readonly IDataProtector _protector;

    /// <summary>
    /// Inicializa una nueva instancia. Permite especificar un directorio base personalizado para tests.
    /// </summary>
    /// <param name="baseDirectory">Directorio base (por defecto el UserProfile del sistema).</param>
    public CredentialStorageService(string? baseDirectory = null)
    {
        string root = string.IsNullOrWhiteSpace(baseDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : baseDirectory;

        _storageDir = Path.Combine(root, ".devbot");
        _apiKeyFilePath = Path.Combine(_storageDir, "api_key");

        var keysDir = new DirectoryInfo(Path.Combine(_storageDir, "protection-keys"));
        var provider = DataProtectionProvider.Create(keysDir, config =>
        {
            config.SetApplicationName("DevBot.Cli");
        });

        _protector = provider.CreateProtector("DevBot.ApiKey.v1");
    }

    /// <inheritdoc />
    public string? LoadApiKey()
    {
        try
        {
            if (!File.Exists(_apiKeyFilePath))
            {
                return null;
            }

            string raw = File.ReadAllText(_apiKeyFilePath).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                // Intento estándar: descifrar el payload protegido
                return _protector.Unprotect(raw);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DevBot] DataProtection Unprotect failed (migrating plaintext fallback): {ex.Message}");

                // Migración transparente: si el archivo estaba en texto plano, protegerlo inmediatamente
                try
                {
                    SaveApiKey(raw);
                }
                catch (Exception saveEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[DevBot] Plaintext migration save failed: {saveEx.Message}");
                }

                return raw;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal error loading API key from '{_apiKeyFilePath}': {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public void SaveApiKey(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        try
        {
            if (!Directory.Exists(_storageDir))
            {
                Directory.CreateDirectory(_storageDir);
            }

            string encrypted = _protector.Protect(apiKey.Trim());
            File.WriteAllText(_apiKeyFilePath, encrypted);

            RestrictFilePermissions(_apiKeyFilePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal error saving API key to '{_apiKeyFilePath}': {ex.Message}");
        }
    }

    private static void RestrictFilePermissions(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var fileInfo = new FileInfo(filePath);
                var security = fileInfo.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                var currentUser = WindowsIdentity.GetCurrent();
                if (currentUser.User != null)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        currentUser.User,
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));
                    fileInfo.SetAccessControl(security);
                }
            }
            else
            {
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DevBot] Non-fatal: Could not restrict file permissions on '{filePath}': {ex.Message}");
        }
    }
}
