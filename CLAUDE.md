# Azure Key Vault Explorer - Development Guidelines

## Project Overview

Fork of [cricketthomas/AzureKeyVaultExplorer](https://github.com/cricketthomas/AzureKeyVaultExplorer) with Azure CLI-based auth replacing MSAL. No Entra ID app registration required.

## Architecture

- **Framework**: .NET 10, Avalonia UI (FluentAvalonia), MVVM with CommunityToolkit.Mvvm
- **Auth**: `Azure.Identity.AzureCliCredential` via `AuthService` -- all az CLI calls go through `/bin/bash -l -c` on macOS (login shell for PATH) using `ProcessStartInfo.ArgumentList` (not `Arguments`) to avoid shell quoting issues
- **Primary implementation**: `src/avalonia/` (Avalonia). The `src/uno/` directory is an upstream alternative -- we don't modify it.
- **Solution**: `src/avalonia/kv.sln`
- **Test project**: `src/avalonia/KeyVaultExplorer.Tests/`

## Development Workflow

### TDD Required

**Every feature or fix MUST follow Test-Driven Development:**

1. Write failing tests first
2. Implement the feature/fix to make tests pass
3. Refactor if needed
4. No PR or release without passing tests

Run tests: `dotnet test src/avalonia/KeyVaultExplorer.Tests/`

### Test Categories

- **Unit tests** (`Tests/Unit/`): Pure logic, no external dependencies. Fast. Always run.
- **Smoke tests** (`Tests/Smoke/`): Require az CLI installed. Verify CLI integration works. Skip gracefully if az unavailable.
- **E2E tests** (`Tests/E2E/`): Require az CLI + active login session. Full workflow tests. Skip gracefully if not authenticated.

### Build & Run Locally

```bash
# Debug (from terminal -- inherits PATH)
cd src/avalonia/Desktop
dotnet run --framework net10.0

# Release .app bundle (for testing Finder launch behavior)
dotnet publish src/avalonia/Desktop/Desktop.csproj -r osx-arm64 -o /tmp/kvtest -c Release -f net10.0 --self-contained
# Then create .app bundle manually (see CI workflow for steps)
```

### Multi-framework Note

Desktop.csproj targets multiple frameworks. Always specify `--framework net10.0` when using `dotnet run`.

## Key Technical Decisions

### Az CLI Process Execution

On macOS, `.app` bundles launched from Finder get a minimal PATH. All `az` commands MUST go through:
```csharp
psi.FileName = "/bin/bash";
psi.ArgumentList.Add("-l");
psi.ArgumentList.Add("-c");
psi.ArgumentList.Add($"az {arguments}");
```
- Use `ArgumentList` (NOT `Arguments` string) to avoid shell quoting issues with nested quotes
- Always read both stdout AND stderr concurrently (`Task.WhenAll`) to prevent deadlocks -- az writes warnings to stderr

### Tenant Discovery

Use the ARM REST API (`az rest --method get --url "https://management.azure.com/tenants?api-version=2022-12-01"`) instead of `az account list` for tenant names. The latter returns null names for tenants without subscriptions.

### "No Subscriptions Found" on Login

`az login --tenant <id>` returns non-zero exit code when a tenant has no subscriptions. This is still a successful authentication -- check stderr for "No subscriptions found" and treat as success.

### Circular Dependency: MainViewModel and ToolBar

`MainViewModel` constructor creates `NavigationFactory` -> `MainPage` -> `TabViewPage` -> `ToolBar`. The ToolBar MUST NOT resolve `MainViewModel` from DI in its constructor (circular). Use `Loaded` event instead.

### Tree View: Flat KV List

The vault tree shows Subscription -> Key Vaults directly (no resource group nesting). The `KvSubscriptionModel.KeyVaultResources` property holds the flat list. The old `ResourceGroups` property still exists for compatibility but the tree XAML binds to `KeyVaultResources`.

### Properties Window on macOS

`ExtendClientAreaToDecorationsHint` must be `false` on the properties AppWindow, otherwise the title bar is hidden and the window can't be dragged.

## File Reference

| Area | Key Files |
|------|-----------|
| Auth | `Services/AuthService.cs` -- AzureCliCredential, tenant discovery, az login |
| Vault ops | `Services/VaultService.cs` -- ARM + KV SDK calls, RBAC grant, IP whitelist |
| Main VM | `ViewModels/MainViewModel.cs` -- tenant switching, auth flow, notifications |
| Tree | `ViewModels/KeyVaultTreeListViewModel.cs` -- vault tree, quick access, pin/unpin |
| Vault page | `ViewModels/VaultPageViewModel.cs` -- secrets/keys/certs list, search/filter, 403 handling |
| Settings | `ViewModels/SettingsPageViewModel.cs` -- tenant selector syncs from MainViewModel |
| Toolbar | `Views/CustomControls/ToolBar.axaml(.cs)` -- tenant dropdown, login spinner |
| Models | `Models/Constants.cs`, `Models/AppSettings.cs`, `Models/KeyVaultModel.cs` |
| CI | `.github/workflows/dotnet.yml` -- builds Win/Mac/Linux, creates GitHub release |

## CI/CD

- Push to `master` triggers `.NET Build and Publish` workflow
- Builds: Windows x64/ARM, macOS ARM, Linux x64
- macOS build creates `.app` bundle with ad-hoc codesigning
- Auto-creates GitHub release with platform zips

## Common Pitfalls

- **Don't use `Arguments` for az commands on macOS** -- use `ArgumentList` to avoid quote escaping nightmares
- **Don't resolve MainViewModel in component constructors** -- use `Loaded` event to avoid circular DI
- **Don't auto-launch `az login` on startup** -- it blocks the UI thread and prevents the window from showing
- **Always read stderr from az processes** -- az writes warnings/errors to stderr; not reading it causes process deadlock
- **Test with .app bundle, not just `dotnet run`** -- the Finder launch environment is very different from terminal
- **`az login` hangs after browser close** -- the process waits for OAuth callback indefinitely. Use `WaitForExitAsync` with a `CancellationTokenSource` timeout (2 min)
- **`az login --tenant` returns non-zero for tenants with no subscriptions** -- check stderr for "No subscriptions found" and treat as success
- **`az account list` returns null tenant names** -- use `az rest` against ARM tenants API (`/tenants?api-version=2022-12-01`) for proper display names
- **ToggleButtons in TextBox InnerRightContent disappear** -- Avalonia's TextBox template replaces inner content. Put toggle buttons outside the TextBox in the DockPanel instead
- **`IsLoggingIn` must be set before any async work on startup** -- otherwise the loading spinner misses the initial load. Set `CenterLoadingPanel.IsVisible=True` as XAML default
- **Don't show "Not Signed In" if tenants were discovered** -- `InitializeAsync` can fail for the saved tenant but the user IS logged in. Only show when `tenants.Count == 0`

## Search System

The vault search (`VaultPageViewModel.KeyVaultFilterHelper`) supports three modes:
1. **Fuzzy (default)**: space-separated terms, all must match. Includes Levenshtein typo tolerance (1 edit for short terms, 2 for 5+ chars)
2. **Regex** (`.*` toggle): full .NET regex with timeout protection
3. **Case sensitive** (`Aa` toggle): works with both fuzzy and regex modes

Fuzzy search splits item text by `-_. /:` delimiters and compares each segment against query terms using Levenshtein distance.
