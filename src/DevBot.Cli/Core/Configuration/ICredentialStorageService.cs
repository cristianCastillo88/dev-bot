namespace DevBot.Cli.Core.Configuration;

/// <summary>
/// Contrato para el almacenamiento y recuperación persistente de credenciales de usuario.
/// </summary>
public interface ICredentialStorageService
{
    /// <summary>
    /// Intenta recuperar la clave de API persistida previamente en el perfil del usuario.
    /// </summary>
    /// <returns>La clave almacenada o null si no existe.</returns>
    string? LoadApiKey();

    /// <summary>
    /// Guarda de forma segura la clave de API en el perfil de usuario.
    /// </summary>
    /// <param name="apiKey">Clave a persistir.</param>
    void SaveApiKey(string apiKey);
}
