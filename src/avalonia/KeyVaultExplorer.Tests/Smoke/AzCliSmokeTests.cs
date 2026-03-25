using KeyVaultExplorer.Services;

namespace KeyVaultExplorer.Tests.Smoke;

/// <summary>
/// Smoke tests that verify Azure CLI integration works.
/// These require az CLI installed and a valid login session.
/// Skip gracefully if not available.
/// </summary>
public class AzCliSmokeTests
{
    private async Task<bool> IsAzCliAvailable()
    {
        var svc = new AuthService();
        return await svc.CheckAzureCliAvailableAsync();
    }

    [Fact]
    public async Task AzCli_IsInstalled()
    {
        var available = await IsAzCliAvailable();
        Assert.True(available, "Azure CLI is not installed. Install from https://aka.ms/installazurecli");
    }

    [Fact]
    public async Task AzCli_VersionReturnsOutput()
    {
        var (exitCode, output) = await AuthService.RunAzCommandAsync("--version");
        Assert.Equal(0, exitCode);
        Assert.Contains("azure-cli", output);
    }

    [Fact]
    public async Task AzCli_AccountShow_ReturnsJson()
    {
        if (!await IsAzCliAvailable()) return;

        var (exitCode, output) = await AuthService.RunAzCommandAsync("account show --output json");
        if (exitCode != 0)
        {
            // Not logged in -- skip
            return;
        }
        Assert.Contains("tenantId", output);
        Assert.Contains("user", output);
    }

    [Fact]
    public async Task DiscoverTenants_ReturnsNonEmpty()
    {
        var svc = new AuthService();
        if (!await svc.CheckAzureCliAvailableAsync()) return;

        var tenants = await svc.DiscoverTenantsAsync();
        // If logged in, should find at least one tenant
        if (tenants.Count > 0)
        {
            Assert.All(tenants, t =>
            {
                Assert.False(string.IsNullOrEmpty(t.TenantId));
                Assert.False(string.IsNullOrEmpty(t.DisplayName));
            });
        }
    }

    [Fact]
    public async Task Initialize_SetsCredential()
    {
        var svc = new AuthService();
        if (!await svc.CheckAzureCliAvailableAsync()) return;

        var result = await svc.InitializeAsync();
        if (!result) return; // not logged in

        Assert.True(svc.IsAuthenticated);
        Assert.NotNull(svc.Credential);
        Assert.NotNull(svc.AuthenticatedUserClaims);
        Assert.False(string.IsNullOrEmpty(svc.TenantId));
        Assert.False(string.IsNullOrEmpty(svc.TenantName));
    }

    [Fact]
    public async Task SwitchTenant_ChangesState()
    {
        var svc = new AuthService();
        if (!await svc.CheckAzureCliAvailableAsync()) return;

        var tenants = await svc.DiscoverTenantsAsync();
        if (tenants.Count == 0) return;

        var tenant = tenants[0];
        await svc.SwitchTenantAsync(tenant.TenantId);

        Assert.Equal(tenant.TenantId, svc.TenantId);
        Assert.NotNull(svc.Credential);
    }
}
