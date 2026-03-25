# Security Policy

## Reporting a Vulnerability

**Do NOT open a public GitHub issue for security vulnerabilities.**

This application interacts with Azure Key Vault, which stores sensitive secrets, keys, and certificates. We take security seriously.

If you discover a security vulnerability, please report it privately:

1. **Email**: istvan-csaba.varga@outlook.com
2. **Subject line**: `[SECURITY] AzureKeyVaultExplorer - <brief description>`
3. **Include**:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

## Response Timeline

- **Acknowledgement**: Within 48 hours
- **Assessment**: Within 1 week
- **Fix**: Dependent on severity, but critical issues will be prioritized

## Scope

The following are in scope:

- Authentication bypass or credential exposure
- Unauthorized access to Key Vault secrets/keys/certificates
- Code injection via process execution (az CLI commands)
- Dependency vulnerabilities with exploitable impact
- Sensitive data exposure in logs, crash dumps, or local storage

The following are out of scope:

- Issues requiring physical access to the user's machine
- Azure Key Vault service-level vulnerabilities (report to [Microsoft MSRC](https://msrc.microsoft.com))
- Social engineering attacks

## Supported Versions

Only the latest release is supported with security updates.

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |
| Older   | No        |

## Security Best Practices for Users

- Keep Azure CLI updated (`az upgrade`)
- Use `az login` with MFA-enabled accounts
- Don't run the app with elevated/root privileges
- Review RBAC assignments granted through the app's "Grant Myself Access" feature
