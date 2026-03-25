using KeyVaultExplorer.Services;

namespace KeyVaultExplorer.Tests.Unit;

public class AuthServiceTests
{
    [Fact]
    public void NewAuthService_IsNotAuthenticated()
    {
        var svc = new AuthService();
        Assert.False(svc.IsAuthenticated);
        Assert.Null(svc.Credential);
        Assert.Null(svc.AuthenticatedUserClaims);
    }

    [Fact]
    public void ClearState_ResetsAllProperties()
    {
        var svc = new AuthService();
        svc.ClearState();

        Assert.False(svc.IsAuthenticated);
        Assert.Null(svc.Credential);
        Assert.Null(svc.TenantId);
        Assert.Null(svc.TenantName);
        Assert.Null(svc.AuthenticatedUserClaims);
    }

    [Fact]
    public async Task RunAzCommandAsync_InvalidCommand_ReturnsNonZero()
    {
        var (exitCode, _) = await AuthService.RunAzCommandAsync("this-is-not-a-real-command");
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task RunAzCommandAsync_Version_ReturnsOutput()
    {
        var (exitCode, output) = await AuthService.RunAzCommandAsync("--version");
        // May fail if az not installed, skip gracefully
        if (exitCode == 0)
        {
            Assert.Contains("azure-cli", output);
        }
    }
}
