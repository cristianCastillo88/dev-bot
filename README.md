# DevBot.Cli 🤖

> **Agente de Ingeniería de Software Autónomo, Quirúrgico y Modular en C# / .NET 8 con Semantic Kernel & Spectre.Console.**

---

## 🚀 Características Principales

- 🔍 **Arquitectura de 4 Subagentes Especializados:**
  - **Scout Agent:** Análisis arquitectónico de solo lectura (`ReadFile`, `ListFiles`, `SearchCode`).
  - **Planner Agent:** Descomposición jerárquica de tareas complejas en hitos atómicos (`plan.json` / `plan.md`) con checkpoints transaccionales en Git.
  - **Coder Agent:** Modificación quirúrgica mediante `ApplyDiff` con tolerancia a indentación y `WriteFile` confinado al hito activo.
  - **Reviewer Agent:** Compilación, ejecución dinámica de pruebas por hito y validación determinista por código de salida (`ExitCode`).
- 🌐 **Soporte Políglota Dinámico:** Detección y adaptación automática para proyectos **.NET (`dotnet`)**, **Node.js (`npm`)**, **Java (`mvn`/`gradle`)** y **Python (`pytest`)**.
- 🎯 **Modos Operacionales Especializados:** `--mode <feature | bug | refactor | test>`.
- 🤝 **Human-in-the-Loop (HITL):** Puerta de aprobación interactiva post-Scout con opción de aclaración o aborto seguro (flag non-interactive `-y`/`--yes` para CI/CD).
- 🔒 **Auditoría de Seguridad Pre-Commit:** `SecretScanner` integrado que bloquea fugas de 7 tipos de secretos (Google, OpenAI, GitHub, AWS, JWT, PEM, Connection Strings) y chequea formateador/linter antes de hacer commit.
- 🧠 **Memoria Persistente y Reglas Locales:** Indexación de mapa de repositorio (`.devbot/repo_map.json`) e inyección de reglas de equipo (`.devbotrules` / `devbot.json`).
- 🛡 **Aislamiento en Git y Rollback Automático:** Trabaja en ramas aisladas (`feature/<slug>-<uuid>`) y revierte el repositorio intacto ante fallos.

---

## 📖 Documentación Completa y Diagramas

Para una explicación exhaustiva sobre el funcionamiento interno, diagramas Mermaid de ciclo de vida y catálogo completo de herramientas, consulta:

👉 **[DOCUMENTACION.md](DOCUMENTACION.md)**

---

## ⚡ Inicio Rápido

### Prerrequisitos
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git CLI instalado y configurado
- Variable de entorno de API Key configurada (`GEMINI_API_KEY` o `OPENAI_API_KEY`)

### Ejecución
```bash
# Compilar la solución
dotnet build

# Ejecutar la suite de pruebas unitarias (80 tests)
dotnet test

# Iniciar una tarea con DevBot
dotnet run --project src/DevBot.Cli -- run "Crear endpoint GET /health con estado del sistema" --mode feature
```
