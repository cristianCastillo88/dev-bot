namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Servicio que gestiona la persistencia de credenciales locales en ~/.devbot/api_key.
/// </summary>
public sealed class CredentialStorageService : ICredentialStorageService
{
    private readonly string _storageDir;
    private readonly string _apiKeyFilePath;

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
    }

    /// <inheritdoc />
    public string? LoadApiKey()
    {
        try
        {
            if (File.Exists(_apiKeyFilePath))
            {
                string key = File.ReadAllText(_apiKeyFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(key)) return key;
            }
        }
        catch
        {
            // Silencioso ante problemas de permisos transitorios
        }
        return null;
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
            File.WriteAllText(_apiKeyFilePath, apiKey.Trim());
        }
        catch
        {
            // Silencioso ante problemas de permisos transitorios
        }
    }
}
