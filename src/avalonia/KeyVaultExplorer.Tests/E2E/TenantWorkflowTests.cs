using KeyVaultExplorer.Models;
using KeyVaultExplorer.Services;

namespace KeyVaultExplorer.Tests.E2E;

/// <summary>
/// End-to-end tests that verify the full tenant switching and vault discovery workflow.
/// Requires az CLI installed, logged in, with access to at least one tenant with key vaults.
/// </summary>
public class TenantWorkflowTests
{
    private async Task<AuthService?> GetAuthenticatedService()
    {
        var svc = new AuthService();
        if (!await svc.CheckAzureCliAvailableAsync()) return null;
        if (!await svc.InitializeAsync()) return null;
        return svc;
    }

    [Fact]
    public async Task FullWorkflow_DiscoverTenants_SwitchTenant_ListSubscriptions()
    {
        var svc = await GetAuthenticatedService();
        if (svc is null) return; // skip if not logged in

        // Step 1: Discover tenants
        var tenants = await svc.DiscoverTenantsAsync();
        Assert.NotEmpty(tenants);

        // Step 2: All tenants have display names (not just IDs)
        foreach (var tenant in tenants)
        {
            Assert.False(string.IsNullOrEmpty(tenant.DisplayName),
                $"Tenant {tenant.TenantId} has no display name");
        }

        // Step 3: Switch to first tenant
        var firstTenant = tenants[0];
        await svc.SwitchTenantAsync(firstTenant.TenantId);
        Assert.Equal(firstTenant.TenantId, svc.TenantId);
        Assert.NotNull(svc.Credential);

        // Step 4: Verify credential can get ARM token (list subscriptions)
        var armToken = await svc.Credential.GetTokenAsync(
            new Azure.Core.TokenRequestContext(["https://management.azure.com/.default"]),
            default);
        Assert.False(string.IsNullOrEmpty(armToken.Token));
    }

    [Fact]
    public async Task SwitchBetweenTenants_MaintainsState()
    {
        var svc = await GetAuthenticatedService();
        if (svc is null) return;

        var tenants = await svc.DiscoverTenantsAsync();
        if (tenants.Count < 2) return; // need at least 2 tenants

        // Switch to tenant A
        await svc.SwitchTenantAsync(tenants[0].TenantId);
        var tenantAId = svc.TenantId;
        var tenantAName = svc.TenantName;

        // Switch to tenant B
        await svc.SwitchTenantAsync(tenants[1].TenantId);
        Assert.NotEqual(tenantAId, svc.TenantId);

        // Switch back to A
        await svc.SwitchTenantAsync(tenants[0].TenantId);
        Assert.Equal(tenantAId, svc.TenantId);
    }

    [Fact]
    public async Task ClearState_ThenReinitialize()
    {
        var svc = await GetAuthenticatedService();
        if (svc is null) return;

        Assert.True(svc.IsAuthenticated);

        svc.ClearState();
        Assert.False(svc.IsAuthenticated);
        Assert.Null(svc.Credential);

        // Re-initialize should work
        var result = await svc.InitializeAsync();
        if (result)
        {
            Assert.True(svc.IsAuthenticated);
            Assert.NotNull(svc.Credential);
        }
    }

    [Fact]
    public async Task VaultService_CanListSubscriptions()
    {
        var svc = await GetAuthenticatedService();
        if (svc is null) return;

        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var db = new KeyVaultExplorer.Database.KvExplorerDb();
        var vaultService = new VaultService(svc, cache, db);

        var subCount = 0;
        await foreach (var sub in vaultService.GetAllSubscriptions())
        {
            Assert.False(string.IsNullOrEmpty(sub.SubscriptionResource.Data.DisplayName));
            subCount++;
            if (subCount >= 3) break; // don't enumerate everything
        }
        // At least some subscriptions should exist for the default tenant
        Assert.True(subCount > 0, "No subscriptions found");
    }
}
