# S1DS-SCOPE

Server-Controlled Organisations, Progression, and Economy for Schedule I dedicated servers using S1DS.

The in-game addon is still named `Organisations`; `S1DS-SCOPE` is the public project name for the repository and documentation.

## What It Changes

- Splits players into organisations with their own membership and ownership rules
- Scopes online balance, property access, customer progression, contracts, and quest progress by organisation or by solo player
- Synchronizes organisation state between the dedicated server and connected clients

## Guides

DocFX source lives under `docfx/`. Build it from this addon directory with:

```bash
docfx docfx/docfx.json
```

Start at `docfx/index.md` for player/admin guides covering how Organisations differs from base co-op behavior.

## Setup

1. Build and install the server and client DLLs for the configurations you use.
2. Install the mod before the server creates or loads its world save.
3. Start the server on a new, empty save with the mod already installed.

## Save Compatibility

`Organisations` is currently intended for fresh saves only.

Do not add this mod to an existing server save. The addon changes how ownership and progression state are scoped and persisted, so loading it into an already-established world will likely produce inconsistent organisation, property, customer, contract, or quest state.

For the safest rollout:

- install the mod first
- create a new server save after installation
- have players join that new save instead of migrating an old one

## Build

```bash
dotnet build Organisations.csproj -c Mono_Server
dotnet build Organisations.csproj -c Mono_Client
dotnet build Organisations.csproj -c Il2cpp_Server
dotnet build Organisations.csproj -c Il2cpp_Client
```

Build output lands under `bin/<Configuration>/<TargetFramework>/`.

For local deployment, copy `local.build.props.example` to `local.build.props` and set your client/server `Mods` folders.
