
<p align="center">
  <img width="280" align="center" src="src\uno\icon-iOS-Default-1024x1024@1x.png">
</p>
<h1 align="center">
  Azure Key Vault Explorer
</h1>
<p align="center">
  Find Key Vaults in Azure faster. No app registration required.
</p>

### Download
Get it from the [releases page](https://github.com/IstvanCsVarga/AzureKeyVaultExplorer/releases) for macOS, Windows, and Linux.

## What's Different in This Fork

This fork replaces the original MSAL app-registration-based authentication with **Azure CLI credentials**, making it suitable for enterprise environments where creating Entra ID app registrations is restricted.

### Changes from upstream
- **No app registration needed** -- uses `AzureCliCredential` from Azure.Identity instead of MSAL
- **Multi-tenant support** -- dynamically discovers all tenants from your `az` session and lets you switch via a dropdown
- **Interactive browser login** -- automatically launches `az login` when switching to a tenant you're not authenticated to (similar to kubelogin interactive)
- **Grant Myself Access** -- right-click a vault to assign Key Vault Secrets User + Reader RBAC roles
- **Whitelist My IP** -- right-click a vault to add your public IP to its firewall rules
- **Actionable error handling** -- 403 errors show a clean card with fix buttons instead of raw error dumps

### Prerequisites
- [Azure CLI](https://aka.ms/installazurecli) installed and on your PATH
- Run `az login` at least once (the app will prompt you via browser when needed)

## Overview

**Key Vault Explorer** is a lightweight tool to simplify finding and accessing secrets, certificates, and keys stored in Azure Key Vault. Originally created by [@cricketthomas](https://github.com/cricketthomas/AzureKeyVaultExplorer).

### Key features

- Multi-tenant switching from the toolbar dropdown
- Browse subscriptions, resource groups, and key vaults per tenant
- Copy secrets to clipboard with auto-clear (configurable up to 60 seconds)
- Pin vaults to Quick Access
- Filter and sort values by name, tags, and content type
- View secret/key/certificate details, versions, and expiry
- Download .pfx and .cer certificate files
- Open vaults directly in Azure Portal
- Light/Dark/System theme support

### Privacy
- **No telemetry or logs collected**
- SQLite database encrypted using DPAPI (Windows) and Keychain (macOS)

## Install

### macOS
Download the `.tar` from releases, extract, and move to Applications:
```bash
tar -xvf keyvaultexplorer.osx-arm64.tar
# If blocked by Gatekeeper:
xattr -cr "osx-arm64/publish/Key Vault Explorer for Azure"
```

### Windows
Download the `.tar` from releases and extract. If Windows blocks it, right-click > Properties > check "Unblock".

### Linux
Download the `.tar` from releases and extract.

## Building from Source

```bash
# Prerequisites: .NET 10 SDK, Azure CLI
cd src/avalonia
dotnet build kv.sln
cd Desktop
dotnet run --framework net10.0
```

## Documentation
- [Building from source](docs/BUILDING.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)

## Acknowledgements

Forked from [cricketthomas/AzureKeyVaultExplorer](https://github.com/cricketthomas/AzureKeyVaultExplorer).

### Dependencies
- **[.NET 10](https://github.com/dotnet/runtime)**
- **[Avalonia](https://github.com/AvaloniaUI/Avalonia/)**
- **[FluentAvalonia](https://github.com/amwx/FluentAvalonia/)**
- **[Azure.Identity](https://github.com/Azure/azure-sdk-for-net)**
