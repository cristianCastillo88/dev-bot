namespace DevBot.Cli.Tools;

/// <summary>
/// Contrato para la interacción y control de operaciones de Git y control de versiones.
/// </summary>
public interface IGitTools
{
    /// <summary>
    /// Determina si el directorio de trabajo actual corresponde a un repositorio Git válido.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Verdadero si está dentro de un árbol de trabajo de Git; de lo contrario, falso.</returns>
    Task<bool> IsGitRepositoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el nombre de la rama Git actual.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Nombre de la rama activa.</returns>
    Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea y cambia a una nueva rama de características.
    /// </summary>
    /// <param name="branchName">Nombre de la nueva rama.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Mensaje descriptivo del resultado.</returns>
    Task<string> CheckoutNewBranch(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cambia a una rama existente.
    /// </summary>
    /// <param name="branchName">Nombre de la rama a seleccionar.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Mensaje descriptivo del resultado.</returns>
    Task<string> CheckoutBranch(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina forzosamente una rama local.
    /// </summary>
    /// <param name="branchName">Nombre de la rama a eliminar.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Mensaje descriptivo del resultado.</returns>
    Task<string> DeleteBranch(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Añade al área de preparación y confirma los archivos especificados con un mensaje de commit.
    /// </summary>
    /// <param name="files">Colección de rutas relativas de los archivos a confirmar.</param>
    /// <param name="message">Mensaje de commit.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Mensaje con el resultado de la confirmación o error detectado.</returns>
    Task<string> CommitFiles(IEnumerable<string> files, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirma todos los cambios en el área de preparación con el mensaje provisto.
    /// </summary>
    /// <param name="message">Mensaje descriptivo del commit.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Mensaje descriptivo del resultado.</returns>
    Task<string> Commit(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Descarta todos los cambios sin confirmar y limpia archivos no rastreados.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Mensaje descriptivo del resultado.</returns>
    Task<string> Revert(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restablece el repositorio a un commit o checkpoint específico descartando cambios intermedios.
    /// </summary>
    /// <param name="commitHash">Hash del commit al que se desea regresar.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Mensaje descriptivo del resultado.</returns>
    Task<string> ResetHardToCheckpointAsync(string commitHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el resumen estadístico de diferencias (diff stat) del último commit.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Texto del diff estadístico.</returns>
    Task<string> GetDiffSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el hash abreviado del último commit en HEAD.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Hash abreviado del commit o 'unknown'.</returns>
    Task<string> GetLastCommitHashAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asegura que el patrón especificado esté excluido localmente en .git/info/exclude sin alterar .gitignore.
    /// </summary>
    /// <param name="pattern">Patrón a excluir (por defecto '.agent/').</param>
    void EnsureGitExclude(string pattern = ".agent/");

    /// <summary>
    /// Publica la rama especificada en el servidor remoto 'origin'.
    /// </summary>
    /// <param name="branchName">Nombre de la rama a empujar.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Tupla con el estado de éxito, salida estándar y errores si existieran.</returns>
    Task<(bool Success, string Output, string Error)> PushBranch(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre un Pull Request utilizando la CLI de GitHub o genera una URL de comparación manual.
    /// </summary>
    /// <param name="branchName">Rama de origen con los cambios.</param>
    /// <param name="baseBranch">Rama de destino receptora.</param>
    /// <param name="title">Título del Pull Request.</param>
    /// <param name="body">Cuerpo descriptivo en Markdown.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>Tupla con éxito, URL del PR y mensaje descriptivo.</returns>
    Task<(bool Success, string PrUrl, string Message)> CreatePullRequestAsync(
        string branchName,
        string baseBranch,
        string title,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera la URL web de comparación de GitHub para abrir un PR manualmente en caso de no contar con CLI autenticado.
    /// </summary>
    /// <param name="branchName">Rama con los cambios.</param>
    /// <param name="baseBranch">Rama base.</param>
    /// <param name="cancellationToken">Token de cancelación para la operación.</param>
    /// <returns>URL de comparación o cadena vacía si no es un remoto GitHub.</returns>
    Task<string> GetGitHubCompareUrlAsync(string branchName, string baseBranch, CancellationToken cancellationToken = default);
}
