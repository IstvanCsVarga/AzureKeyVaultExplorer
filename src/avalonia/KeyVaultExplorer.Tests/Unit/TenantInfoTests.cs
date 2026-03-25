using KeyVaultExplorer.Models;

namespace KeyVaultExplorer.Tests.Unit;

public class TenantInfoTests
{
    [Fact]
    public void TenantInfo_Record_Equality()
    {
        var a = new TenantInfo("abc-123", "Test Tenant");
        var b = new TenantInfo("abc-123", "Test Tenant");
        Assert.Equal(a, b);
    }

    [Fact]
    public void TenantInfo_Record_Inequality()
    {
        var a = new TenantInfo("abc-123", "Test Tenant");
        var b = new TenantInfo("def-456", "Other Tenant");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TenantInfo_Properties()
    {
        var t = new TenantInfo("abc-123", "My Tenant");
        Assert.Equal("abc-123", t.TenantId);
        Assert.Equal("My Tenant", t.DisplayName);
    }

    [Fact]
    public void AuthenticatedUserClaims_Properties()
    {
        var claims = new AuthenticatedUserClaims
        {
            Username = "user@test.com",
            TenantId = "abc-123",
            Email = "user@test.com",
            Name = "Test User",
            TenantDisplayName = "My Tenant"
        };
        Assert.Equal("user@test.com", claims.Username);
        Assert.Equal("abc-123", claims.TenantId);
        Assert.Equal("My Tenant", claims.TenantDisplayName);
    }

    [Fact]
    public void AppSettings_Defaults()
    {
        var settings = new AppSettings();
        Assert.False(settings.BackgroundTransparency);
        Assert.Equal(30, settings.ClipboardTimeout);
        Assert.Equal("System", settings.AppTheme);
        Assert.Equal(string.Empty, settings.SelectedTenantId);
    }
}
