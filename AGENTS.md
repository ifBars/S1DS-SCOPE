# AGENTS.md

## Project Context

`Organisations` is a Schedule I dedicated-server addon built on the S1DS framework mod.

- Framework mod source: `..\..\DedicatedServerMod`
- This addon references it through `..\..\DedicatedServerMod\DedicatedServerMod.csproj`.
- Keep framework-level behavior and API assumptions aligned with that source before changing addon integrations.

## Code Boundaries

- `Server/` is server-only behavior and Harmony patching.
- `Client/` is client-only UI, state, and patching.
- `Contracts/` contains custom-message DTOs shared across the wire.
- `Domain/`, `Services/`, `Persistence/`, `Configuration/`, and `Utils/` hold shared addon logic.
- Guard side-specific code with `#if SERVER`, `#if CLIENT`, `#if MONO`, and `#if IL2CPP` as appropriate.

## Development Rules

- Use `bun` for JavaScript package management if JS tooling is introduced.
- Do not change Cargo output locations or set `CARGO_TARGET_DIR` unless explicitly asked.
- Keep public API additions intentional and documented with XML comments.
- Prefer small, side-aware changes over broad framework or lifecycle rewrites.
- Treat this addon as fresh-save oriented unless the user explicitly asks for migration support.

## Validation

Run the narrowest useful checks for the touched side/runtime:

```powershell
dotnet build Organisations.csproj -c Mono_Server -p:AutomateLocalDeployment=false
dotnet build Organisations.csproj -c Mono_Client -p:AutomateLocalDeployment=false
dotnet restore Organisations.csproj -p:Configuration=Il2cpp_Server -p:EffectiveConfiguration=Il2cpp_Server -p:TargetFramework=net6.0
dotnet build Organisations.csproj -c Il2cpp_Server -p:AutomateLocalDeployment=false --no-restore
dotnet restore Organisations.csproj -p:Configuration=Il2cpp_Client -p:EffectiveConfiguration=Il2cpp_Client -p:TargetFramework=net6.0
dotnet build Organisations.csproj -c Il2cpp_Client -p:AutomateLocalDeployment=false --no-restore
```

For join/sync behavior, use:

```powershell
.\tests\Run-JoinSmokeTest.ps1 -BuildAndDeploy
```
