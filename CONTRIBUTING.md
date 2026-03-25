# Contributing to Azure Key Vault Explorer

Thank you for your interest in contributing! This guide covers everything you need to get started.

## Before You Start

- **Bug fixes**: Open an issue first (or find an existing one), then submit a PR referencing it.
- **New features**: Open a feature request issue and **discuss with maintainers before writing code**. This avoids wasted effort on features that may not align with the project direction.
- **Small improvements** (typos, docs, minor refactors): Go ahead and open a PR directly.

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://aka.ms/installazurecli) (`az login` for auth)
- Git

### Building

```bash
# Clone your fork
git clone https://github.com/<your-username>/AzureKeyVaultExplorer.git
cd AzureKeyVaultExplorer

# Build
dotnet build src/avalonia/kv.sln

# Run (must specify framework)
cd src/avalonia/Desktop
dotnet run --framework net10.0
```

### Running Tests

```bash
dotnet test src/avalonia/KeyVaultExplorer.Tests/
```

Tests are organized in three tiers:

| Tier | Directory | Requirements | Speed |
|------|-----------|-------------|-------|
| Unit | `Tests/Unit/` | None | Fast |
| Smoke | `Tests/Smoke/` | Azure CLI installed | Medium |
| E2E | `Tests/E2E/` | Azure CLI + active `az login` session | Slow |

Smoke and E2E tests skip gracefully when prerequisites aren't met.

## Development Workflow (TDD Required)

All features and fixes **must** follow Test-Driven Development:

1. **Write a failing test** that demonstrates the expected behavior
2. **Implement** the minimum code to make the test pass
3. **Refactor** if needed, keeping tests green
4. Submit your PR

PRs without tests will not be merged unless the change is purely cosmetic (docs, comments, formatting).

## Commit Conventions

We follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>: <short description>

[optional body]

[optional footer]
```

**Types:**
- `feat:` -- new feature
- `fix:` -- bug fix
- `docs:` -- documentation only
- `refactor:` -- code change that neither fixes a bug nor adds a feature
- `test:` -- adding or updating tests
- `ci:` -- CI/CD changes
- `chore:` -- maintenance (dependencies, tooling)

**Examples:**
```
feat: add regex toggle to vault search
fix: az login hangs after browser close on macOS
docs: update README install instructions
test: add fuzzy search unit tests for Levenshtein matching
```

Keep commits atomic -- one logical change per commit.

## Pull Request Process

1. **Fork** the repo and create a branch from `master`
   - Branch naming: `feat/description`, `fix/description`, `docs/description`
2. **Write tests first**, then implement your change
3. **Run the full test suite** locally: `dotnet test src/avalonia/KeyVaultExplorer.Tests/`
4. **Push** your branch and open a PR against `master`
5. Fill out the PR template (summary, testing, breaking changes)
6. Wait for CI to pass and a maintainer review

### PR Guidelines

- Keep PRs focused -- one feature or fix per PR
- Don't include unrelated changes (formatting, refactoring) in the same PR
- Update documentation if your change affects user-facing behavior
- Add screenshots/recordings for UI changes
- Reference the issue your PR addresses (`Fixes #123`)

## Code Style

- Follow the `.editorconfig` in the repo root (enforced by IDE)
- Use file-scoped namespaces (`namespace Foo;` not `namespace Foo { }`)
- MVVM pattern with `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
- Private fields: `_camelCase`
- Properties/Methods: `PascalCase`
- No `var` for built-in types; `var` is acceptable when the type is obvious

## Architecture Notes

This is an **Avalonia UI** desktop app. Key things to know:

- **We only modify `src/avalonia/`**. The `src/uno/` directory is the upstream alternative -- don't touch it.
- **Auth goes through Azure CLI** (`AzureCliCredential`), not MSAL. No app registrations.
- **All `az` CLI calls** must go through `/bin/bash -l -c` on macOS (login shell for PATH). Use `ProcessStartInfo.ArgumentList`, never `Arguments`.
- See `CLAUDE.md` for detailed technical decisions and pitfalls.

## Reporting Issues

- Use the **Bug Report** or **Feature Request** issue templates
- For security vulnerabilities, see [SECURITY.md](SECURITY.md) -- do **not** open a public issue

## Getting Help

- Open a [Discussion](https://github.com/IstvanCsVarga/AzureKeyVaultExplorer/discussions) for questions
- Tag issues with `help-wanted` or `good first issue` for contribution opportunities
