namespace DevBot.Cli.Core.Strategies;

/// <summary>
/// Tipos de stacks tecnológicos y ecosistemas de compilación soportados por DevBot.
/// </summary>
public enum ProjectStackType
{
    /// <summary>Ecosistema .NET / C# (vía dotnet CLI).</summary>
    DotNet,

    /// <summary>Ecosistema JavaScript / TypeScript / Node.js (vía npm/yarn/pnpm).</summary>
    NodeJs,

    /// <summary>Ecosistema Java (vía Apache Maven).</summary>
    JavaMaven,

    /// <summary>Ecosistema Python (vía pytest / pip).</summary>
    Python,

    /// <summary>Ecosistema Go (vía go build / go test).</summary>
    Go,

    /// <summary>Ecosistema no reconocido o fallback genérico.</summary>
    Unknown
}
