# DevBot.Cli - Manual Técnico de Arquitectura y Funcionamiento

**DevBot.Cli** es un asistente de ingeniería de software autónomo y modular desarrollado en **C# y .NET 8**, impulsado por **Microsoft Semantic Kernel**, **Google Gemini**, estilizado con **Spectre.Console** y orquestado mediante **Inyección de Dependencias** formal con `Microsoft.Extensions.DependencyInjection`.

Su propósito es ejecutar tareas de desarrollo quirúrgicas sobre repositorios de código real: exploración arquitectónica, descomposición en hitos, codificación de cambios mínimos, ejecución y validación determinista de pruebas unitarias, auditoría de seguridad pre-commit y apertura de Pull Requests listos para revisión humana.

---

## 📑 Tabla de Contenidos
1. [Arquitectura General y Ciclo Agéntico](#1-arquitectura-general-y-ciclo-agéntico)
2. [Inyección de Dependencias y Composition Root](#2-inyección-de-dependencias-y-composition-root)
3. [Subagentes Especializados](#3-subagentes-especializados)
4. [Catálogo de Herramientas (Tools)](#4-catálogo-de-herramientas-tools)
5. [Modos Operacionales (`--mode`)](#5-modos-operacionales---mode)
6. [Sistema de Memoria Persistente y Reglas Locales](#6-sistema-de-memoria-persistente-y-reglas-locales)
7. [Estrategias y Detección de Stack Políglota](#7-estrategias-y-detección-de-stack-políglota)
8. [Interacción con Git, Auditoría Pre-Commit y Rollback](#8-interacción-con-git-auditoría-pre-commit-y-rollback)
9. [Auditoría de Seguridad y Detección de Secretos](#9-auditoría-de-seguridad-y-detección-de-secretos)
10. [Human-in-the-Loop (HITL)](#10-human-in-the-loop-hitl)
11. [Guía Rápida de Uso en Línea de Comandos](#11-guía-rápida-de-uso-en-línea-de-comandos)

---

## 1. Arquitectura General y Ciclo Agéntico

DevBot implementa un flujo de ciclo de vida orquestado por `Orchestrator.cs`. El flujo garantiza que ningún cambio se integre a la rama principal sin validación estricta de compilación, ejecución de tests y análisis de seguridad.

### Diagrama de Ciclo de Vida Completo

![Diagrama de Ciclo de Vida Agéntico](docs/diagrams/1_ciclo_vida.svg)

---

## 2. Inyección de Dependencias, Factory Pattern y Composition Root

A partir de la versión 1.5+, DevBot implementa formalmente el principio de **Inversión de Dependencias (DIP)** y el **Patrón Factory** mediante `Microsoft.Extensions.DependencyInjection`:

### Composition Root (`Program.cs`)
`Program.cs` actúa como el único punto de composición de la aplicación:
1. Parsea y valida opciones tipadas con `CliArgumentsParser`.
2. Registra los contratos e implementaciones del dominio mediante el método de extensión `services.AddDevBotCore()`.
3. Resuelve el orquestador desacopladamente a través de `IOrchestratorFactory` (`serviceProvider.GetRequiredService<IOrchestratorFactory>().Create(context)`), eliminando cualquier invocación reflexiva o Service Locator.
4. Gestiona la cancelación cooperativa (Ctrl+C) asegurando la desuscripción del evento en bloque `finally`.

### Ciclos de Vida Registrados:
- **Singleton:** `IProjectStackDetector`, `IRepositoryMemoryService`, `IPreCommitAuditor`, `ICredentialStorageService`.
- **Transient (Interfaces y Subagentes):** `IScoutAgent` / `ScoutAgent`, `IPlannerAgent` / `PlannerAgent`, `ICoderAgent` / `CoderAgent`, `IReviewerAgent` / `ReviewerAgent`.
- **Transient (Orquestación):** `IOrchestratorFactory` / `OrchestratorFactory`, `Orchestrator`.

---

## 3. Subagentes Especializados e Interfaces

DevBot divide las responsabilidades en subagentes con contextos y herramientas estrictamente delimitados bajo el principio de mínimo privilegio e inversión de dependencias:

| Subagente / Interfaz | Responsabilidad Principal | Herramientas Asignadas | Salida Generada |
| :--- | :--- | :--- | :--- |
| **`IScoutAgent`** (`ScoutAgent`) | Analiza la arquitectura del repositorio, ubica archivos clave y redacta el plano técnico. Es **estrictamente de solo lectura**. | `ReadFile`, `ListFiles`, `SearchCode` | `.agent/scout_report.md` |
| **`IPlannerAgent`** (`PlannerAgent`) | Descompone tareas complejas de abajo hacia arriba (*Bottom-Up*) en hitos atómicos (máx 2-3 archivos) con comandos de verificación. | `ReadFile`, `ListFiles` | `.agent/plan.json`, `.agent/plan.md` |
| **`ICoderAgent`** (`CoderAgent`) | Aplica modificaciones quirúrgicas y mínimas confinadas al alcance del hito activo respetando `.devbotrules`. | `ReadFile`, `WriteFile`, `ApplyDiff` | Archivos modificados en disco |
| **`IReviewerAgent`** (`ReviewerAgent`) | Ejecuta el comando de verificación específico del hito (`VerificationCommand`), captura errores y valida `ExitCode == 0`. | `RunCommand`, `ReadFile` | `.agent/review_results.md` |

---

## 4. Catálogo de Herramientas (Tools)

Las herramientas son funciones nativas de C# decoradas con `[KernelFunction]` que Semantic Kernel expone al LLM en tiempo de ejecución.

### Matriz de Herramientas por Agente

![Matriz de Subagentes y Herramientas](docs/diagrams/2_subagentes_tools.svg)

### Detalle Técnico de Cada Herramienta

#### 1. `ReadFile(string filePath, int? startLine, int? endLine)`
- **Uso:** Lectura segura de código. Permite leer archivos enteros o fragmentos delimitados por rangos de líneas.
- **Optimización:** Previene saturación de la ventana de contexto al inspeccionar métodos específicos.

#### 2. `ListFiles(string directoryPath, string searchPattern)`
- **Uso:** Inspección del árbol de directorios.
- **Protección:** Excluye automáticamente directorios de dependencias o compilación pesados (`.git`, `node_modules`, `bin`, `obj`, `.agent`).

#### 3. `SearchCode(string query, string filePattern)`
- **Uso:** Búsqueda textual o por patrones en el código base.
- **Optimización:** Streaming con `File.ReadLines` en una sola pasada para evitar sobrecarga de memoria RAM.

#### 4. `ApplyDiff(string filePath, string originalSnippet, string replacementSnippet)`
- **Uso:** Modificación quirúrgica de archivos existentes.
- **Robustez:** Algoritmo con **tolerancia a espacios en blanco e indentación** que localiza el bloque objetivo aun ante pequeñas variaciones en espacios/tabs o saltos de línea (`\r\n` vs `\n`).

#### 5. `WriteFile(string filePath, string content)`
- **Uso:** Creación de nuevos archivos o reemplazo completo cuando se requiere un archivo nuevo desde cero.
- **Seguridad:** Crea los directorios padre de forma recursiva si no existen.

#### 6. `TerminalTools.RunCommand(string command, string workingDirectory)`
- **Uso:** Ejecución de comandos del sistema operativo (compilación, pruebas unitarias, linters).
- **Protección contra Deadlocks:** Cierra inmediatamente `StandardInput` para evitar que comandos interactivos bloqueen el proceso. Implementa timeout configurable.

---

## 5. Modos Operacionales (`--mode`)

DevBot ajusta el comportamiento y las directrices de los prompts según el objetivo de la misión:

![Modos Operacionales Especializados](docs/diagrams/3_modos_operacionales.svg)

### Auto-Detección y Forzado Manual (Override)
- **Auto-Detección (`ModePolicyRegistry.DetectMode`):** Analiza semánticamente la descripción de la tarea antes de iniciar. Si detecta términos de bug activa `bug`; si detecta pruebas activa `test`; si detecta refactorización activa `refactor`; de lo contrario adopta `feature`.
- **Forzado Manual:** Anula la detección automática indicando `--mode <feature|bug|refactor|test>`.

### Tabla Comparativa de Políticas

| Modo | Política Scout | Política Coder | Política Reviewer | Prefijo Commit |
| :--- | :--- | :--- | :--- | :--- |
| **`feature`** | Identifica puntos de extensión y diseño modular. | Diseña soluciones limpias y extensibles con sus tests. | Valida compatibilidad y que se añadan pruebas de la nueva funcionalidad. | `feat:` |
| **`bug`** | Identifica el punto exacto de falla y la causa raíz. | Aplica el cambio mínimo posible para corregir el defecto sin efectos secundarios. | Exige una prueba de regresión que falle sin el parche y pase con él. | `fix:` |
| **`refactor`** | Identifica código duplicado o deuda técnica sin modificar contratos públicos. | Prohibido alterar firmas públicas, endpoints o contratos de API. | Exige que todas las pruebas previas pasen al 100%. | `refactor:` |
| **`test`** | Mapea código no cubierto, ramas condicionales y casos extremos. | Solo añade o complementa archivos de prueba (`*Test*.cs`, `*.spec.ts`, etc.). | Verifica aumento efectivo de cobertura y calidad de aserciones. | `test:` |

---

## 6. Sistema de Memoria Persistente y Reglas Locales

![Sistema de Memoria Persistente y Reglas Locales](docs/diagrams/4_memoria_reglas.svg)

### 1. Reglas Locales (`.devbotrules` o `devbot.json`)
Permite al equipo definir directrices técnicas que DevBot respetará rigurosamente:
```markdown
# .devbotrules
- Usar siempre inyección de dependencias por constructor.
- No utilizar System.Console directamente, utilizar ILogger.
- Mantener funciones menores a 40 líneas.
- Toda entidad de dominio debe ser inmutable.
```

### 2. Mapa del Repositorio (`.devbot/repo_map.json`)
Si el commit hash del repositorio no ha cambiado desde la última ejecución, DevBot reutiliza el mapa en caché, reduciendo drásticamente el consumo de tokens y el tiempo de análisis.

---

## 7. Estrategias y Detección de Stack Políglota

DevBot detecta automáticamente el stack tecnológico del proyecto mediante `ProjectStackDetector`:

![Detección de Stack Políglota y Estrategias](docs/diagrams/5_stack_detector.svg)

Cada estrategia (`DotNetStrategy`, `NodeNpmStrategy`, `MavenStrategy`, `PythonStrategy`) proporciona comandos nativos de build, test y linter, junto con guías idiomáticas específicas para el CoderAgent.

---

## 8. Interacción con Git, Auditoría Pre-Commit y Rollback

![Flujo Git, Auditoría de Seguridad y Rollback](docs/diagrams/6_git_rollback.svg)

### Contrato Desacoplado `IGitTools` y Cancelación Cooperativa
Todas las operaciones de Git y GitHub CLI se abstraen detrás de la interfaz [`IGitTools`](file:///c:/Users/pc/Desktop/Proyectos/BotCLI/src/DevBot.Cli/Tools/IGitTools.cs) implementada por [`GitTools`](file:///c:/Users/pc/Desktop/Proyectos/BotCLI/src/DevBot.Cli/Tools/GitTools.cs):
- **Cancelación Cooperativa Real:** Los métodos asíncronos (`IsGitRepositoryAsync`, `CheckoutNewBranch`, `Commit`, `Revert`, `PushBranch`, etc.) enlazan el `CancellationToken` del usuario con el timeout interno mediante `CancellationTokenSource.CreateLinkedTokenSource`. Si el usuario presiona `Ctrl+C`, el proceso Git se destruye de inmediato (`process.Kill(true)`) evitando procesos huérfanos.
- **Aislamiento de Telemetría:** `EnsureGitExclude` añade `.agent/` a `.git/info/exclude` sin alterar el `.gitignore` del repositorio del usuario.

### Mecanismo de Rollback y Checkpoints
- Cada hito completado y verificado genera un commit de checkpoint en la rama temporal `feature/...`.
- Si un hito falla tras agotar sus reintentos (`MaxRetries`), DevBot ejecuta un rollback atómico al último checkpoint verde con `ResetHardToCheckpointAsync`.
- Si no se completó ningún hito, DevBot restaura la rama original y elimina la rama temporal huérfana.

---

## 9. Auditoría de Seguridad y Detección de Secretos

Antes de crear cualquier commit, `SecretScanner.cs` inspecciona línea por línea el contenido de todos los archivos modificados:

| Regla | Tipo de Secreto | Patrón / Firma |
| :--- | :--- | :--- |
| **`SEC001`** | Google Cloud / Gemini API Keys | `AIza[0-9A-Za-z\-_]{35}`, `AQ\.[0-9A-Za-z\-_]{35}` |
| **`SEC002`** | OpenAI / Anthropic Secret Keys | `sk-[A-Za-z0-9\-_]{20,}` |
| **`SEC003`** | GitHub Personal Access Tokens | `ghp_[0-9A-Za-z]{36}`, `gho_...` |
| **`SEC004`** | AWS Access Key ID | `AKIA[0-9A-Z]{16}` |
| **`SEC005`** | JSON Web Tokens (JWT) | `eyJ[A-Za-z0-9-_=]+\.eyJ[A-Za-z0-9-_=]+\.[A-Za-z0-9-_.+/=]*` |
| **`SEC006`** | Claves Privadas PEM | `-----BEGIN (RSA \|EC \|DSA \|OPENSSH \|)PRIVATE KEY-----` |
| **`SEC007`** | Contraseñas en Cadenas de Conexión | `(Password\|pwd\|User Id)=[^;]+` |

### Enmascaramiento Seguro (Redaction)
Cuando se detecta un hallazgo, el valor del secreto **jamás se imprime en texto claro**:
```text
Valor original:  AIzaSyD-1234567890abcdefghijklmnopqrstuv
Valor redactado: AIzaSyD-1234****...****klmnopqrstuv
```

---

## 10. Human-in-the-Loop (HITL)

Tras la fase de exploración del Scout Agent, DevBot detiene la ejecución y extrae un resumen ejecutivo del plan:
- **Archivos a modificar o crear.**
- **Enfoque técnico sugerido.**
- **Comandos de verificación que se ejecutarán.**

El desarrollador tiene 3 opciones interactivas:
1. **Aprobar:** DevBot procede inmediatamente a la fase Coder.
2. **Aclarar / Modificar:** El desarrollador ingresa texto con directrices adicionales. DevBot inyecta esta directriz en el Coder.
3. **Abortar:** DevBot cancela la ejecución, realiza rollback y limpia la rama de trabajo.

Para entornos CI/CD desatendidos, el flag `--yes` / `-y` aprueba automáticamente el plan.

---

## 11. Guía Rápida de Uso en Línea de Comandos

```bash
# 1. Ejecutar una tarea con confirmación interactiva humana (Modo Feature por defecto)
devbot -t "Agregar endpoint GET /api/v1/health con métricas de memoria"

# 2. Ejecutar corrección de un bug en modo interactivo
devbot -t "Corregir NullReferenceException en InvoiceService" --mode bug

# 3. Ejecutar refactorización en modo automático (para CI/CD o scripts)
devbot -t "Separar responsabilidades de OrderProcessor a OrderValidator" --mode refactor --yes

# 4. Generar nuevas pruebas unitarias para aumentar cobertura
devbot -t "Agregar pruebas unitarias para PaymentGateway" -M test -y

# 5. Ver el último informe ejecutivo generado
devbot --report

# 6. Ver las últimas líneas del log de auditoría
devbot --logs

# 7. Ver ayuda y opciones disponibles
devbot --help
```
