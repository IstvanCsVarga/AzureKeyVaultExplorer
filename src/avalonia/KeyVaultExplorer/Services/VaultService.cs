using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.Resources;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using KeyVaultExplorer.Database;
using KeyVaultExplorer.Exceptions;
using KeyVaultExplorer.Models;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KeyVaultExplorer.Services;
/* Call me a bad person for abstracting away/wrapping a library already doing all the work. */

public partial class VaultService
{
#pragma warning disable IDE0290 // Use primary constructor

    public VaultService(AuthService authService, IMemoryCache memoryCache, KvExplorerDb dbContext)
#pragma warning restore IDE0290 // Use primary constructor
    {
        _authService = authService;
        _memoryCache = memoryCache;
        _dbContext = dbContext;
    }

    private AuthService _authService { get; set; }
    private KvExplorerDb _dbContext { get; set; }
    private IMemoryCache _memoryCache { get; set; }

    public static async IAsyncEnumerable<KeyVaultResource> GetWithKeyVaultsBySubscriptionAsync(KvSubscriptionModel resource)
    {
        await foreach (var kvResource in resource.Subscription.GetKeyVaultsAsync())
        {
            yield return kvResource;
        }
    }

    public async Task<KeyVaultKey> CreateKey(KeyVaultKey key, Uri KeyVaultUri)
    {
        var client = new KeyClient(KeyVaultUri, _authService.Credential);
        return await client.CreateKeyAsync(key.Name, key.KeyType);
    }

    public async Task<KeyVaultSecret> CreateSecret(KeyVaultSecret secret, Uri KeyVaultUri)
    {
        var client = new SecretClient(KeyVaultUri, _authService.Credential);
        return await client.SetSecretAsync(secret);
    }

    public async IAsyncEnumerable<SubscriptionResourceWithNextPageToken> GetAllSubscriptions(CancellationToken cancellationToken = default, string continuationToken = null)
    {
        var armClient = new ArmClient(_authService.Credential);
        var subscriptionsPageable = armClient.GetSubscriptions().GetAllAsync(cancellationToken).AsPages(continuationToken);

        await foreach (var subscription in subscriptionsPageable)
        {
            foreach (var subscriptionResource in subscription.Values)
            {
                yield return new SubscriptionResourceWithNextPageToken(subscriptionResource, subscription.ContinuationToken);
            }
        }
    }

    public async Task<KeyVaultCertificateWithPolicy> GetCertificate(Uri kvUri, string name)
    {
        var client = new CertificateClient(kvUri, _authService.Credential);
        try
        {
            var response = await client.GetCertificateAsync(name);
            return response;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            throw new KeyVaultItemNotFoundException(ex.Message, ex);
        }
    }

    public async Task<List<CertificateProperties>> GetCertificateProperties(Uri kvUri, string name)
    {
        var client = new CertificateClient(kvUri, _authService.Credential);
        List<CertificateProperties> list = new();
        try
        {
            var response = client.GetPropertiesOfCertificateVersionsAsync(name);
            await foreach (CertificateProperties item in response)
            {
                list.Add(item);
            }
            return list;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            throw new KeyVaultItemNotFoundException(ex.Message, ex);
        }
    }

    public async Task<KeyVaultKey> GetKey(Uri kvUri, string name)
    {
        var client = new KeyClient(kvUri, _authService.Credential);
        try
        {
            var response = await client.GetKeyAsync(name);
            return response;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            throw new KeyVaultItemNotFoundException(ex.Message, ex);
        }
    }

    public async Task<List<KeyProperties>> GetKeyProperties(Uri kvUri, string name)
    {
        var client = new KeyClient(kvUri, _authService.Credential);
        List<KeyProperties> list = new();
        try
        {
            var response = client.GetPropertiesOfKeyVersionsAsync(name);
            await foreach (KeyProperties item in response)
            {
                list.Add(item);
            }
            return list;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            throw new KeyVaultItemNotFoundException(ex.Message, ex);
        }
    }

    public async IAsyncEnumerable<KeyVaultResource> GetKeyVaultResource()
    {
        var armClient = new ArmClient(_authService.Credential);

        var subscription = await armClient.GetDefaultSubscriptionAsync();
        await foreach (var kvResource in subscription.GetKeyVaultsAsync())
        {
            yield return kvResource;
        }
    }


    public async Task<KeyVaultResource> GetKeyVaultResource(string subscriptionId, string resourceGroupName, string vaultName)
    {
        var client = new ArmClient(_authService.Credential);
        var resourceIdentifier = KeyVaultResource.CreateResourceIdentifier(subscriptionId: subscriptionId, resourceGroupName: resourceGroupName, vaultName: vaultName);
        return await client.GetKeyVaultResource(resourceIdentifier).GetAsync();
    }


    /// <summary>
    /// returns all key vaults based on all the subscriptions the user has rights to view.
    /// </summary>
    /// <returns></returns>
    public async IAsyncEnumerable<KvSubscriptionModel> GetKeyVaultResourceBySubscription()
    {
        var armClient = new ArmClient(_authService.Credential);

        var placeholder = new KeyVaultResourcePlaceholder();
        var rgPlaceholder = new KvResourceGroupModel() //needed to show chevron
        {
            KeyVaultResources = [placeholder],
            //ResourceGroupDisplayName = string.Empty
        };

        var subscriptions = await _memoryCache.GetOrCreateAsync($"subscriptions_{_authService.TenantId}", async (f) =>
        {
            f.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);

            var savedSubscriptions = await _dbContext.GetStoredSubscriptions(_authService.TenantId ?? null);
            List<SubscriptionResource> subscriptionCollection = [];
            foreach (var sub in savedSubscriptions)
            {
                var sr = await armClient.GetSubscriptionResource(SubscriptionResource.CreateResourceIdentifier(sub.SubscriptionId)).GetAsync();
                subscriptionCollection.Add(sr.Value);
            }
            if (subscriptionCollection.Any())
                return subscriptionCollection;

            return armClient.GetSubscriptions().AsEnumerable();
        });

        //foreach (var subscription in armClient.GetSubscriptions())
        foreach (var subscription in subscriptions)
        {
            var resource = new KvSubscriptionModel
            {
                SubscriptionDisplayName = subscription.Data.DisplayName,
                SubscriptionId = subscription.Data.Id,
                Subscription = subscription,
                ResourceGroups = [rgPlaceholder]
            };
            yield return resource;
        }
    }

    public async IAsyncEnumerable<KeyVaultResource> GetKeyVaultResources()
    {
        var armClient = new ArmClient(_authService.Credential);
        foreach (var subscription in armClient.GetSubscriptions().ToArray())
        {
            await foreach (var kvResource in subscription.GetKeyVaultsAsync())
            {
                yield return kvResource;
            }
        }
    }

    public async IAsyncEnumerable<KeyVaultResource> GetKeyVaultsByResourceGroup(ResourceGroupResource resource)
    {
        var armClient = new ArmClient(_authService.Credential);

        await foreach (var kvResource in resource.GetKeyVaults())
        {
            yield return kvResource;
        }
    }

    public async IAsyncEnumerable<KeyVaultResource> GetKeyVaultsBySubscription(KvSubscriptionModel resource)
    {
        var armClient = new ArmClient(_authService.Credential);
        resource.Subscription = armClient.GetSubscriptionResource(resource.Subscription.Id);

        foreach (var kvResource in resource.Subscription.GetKeyVaults())
        {
            yield return kvResource;
        }
    }

    public async IAsyncEnumerable<ResourceGroupResource> GetResourceGroupBySubscription(KvSubscriptionModel resource)
    {
        var armClient = new ArmClient(_authService.Credential);
        resource.Subscription = armClient.GetSubscriptionResource(resource.Subscription.Id);

        foreach (var kvResourceGroup in resource.Subscription.GetResourceGroups())
        {
            yield return kvResourceGroup;
        }
    }

    public async Task<KeyVaultSecret> GetSecret(Uri kvUri, string secretName)
    {
        var client = new SecretClient(kvUri, _authService.Credential);
        try
        {
            var secret = await client.GetSecretAsync(secretName, cancellationToken: CancellationToken.None);
            return secret;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            throw new KeyVaultItemNotFoundException(ex.Message, ex);
        }
        catch (Exception ex) when (ex.Message.Contains("403"))
        {
            throw new KeyVaultInsufficientPrivilegesException(ex.Message, ex);
        }
    }

    public async Task<List<SecretProperties>> GetSecretProperties(Uri keyVaultUri, string name)
    {
        var client = new SecretClient(keyVaultUri, _authService.Credential);
        List<SecretProperties> list = new();
        try
        {
            var response = client.GetPropertiesOfSecretVersionsAsync(name);
            await foreach (SecretProperties item in response)
            {
                list.Add(item);
            }
            return list;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            throw new KeyVaultItemNotFoundException(ex.Message, ex);
        }
    }

    public async Task<Dictionary<string, KeyVaultResource>> GetStoredSelectedSubscriptions(string subscriptionId)
    {
        var resource = new ResourceIdentifier(subscriptionId);
        var armClient = new ArmClient(_authService.Credential);
        SubscriptionResource subscription = armClient.GetSubscriptionResource(resource);

        var vaults = subscription.GetKeyVaultsAsync();
        Dictionary<string, KeyVaultResource> savedSubs = [];
        await foreach (var vault in vaults)
        {
            savedSubs.Add(resource.SubscriptionId!, vault);
        }

        return savedSubs;
    }

    public record SubscriptionResourceWithNextPageToken(SubscriptionResource SubscriptionResource, string ContinuationToken);

    public async IAsyncEnumerable<CertificateProperties> GetVaultAssociatedCertificates(Uri kvUri)
    {
        var client = new CertificateClient(kvUri, _authService.Credential);
        await foreach (var certProperties in client.GetPropertiesOfCertificatesAsync())
        {
            yield return certProperties;
        }
    }

    public async IAsyncEnumerable<KeyProperties> GetVaultAssociatedKeys(Uri kvUri)
    {
        var client = new KeyClient(kvUri, _authService.Credential);
        await foreach (var keyProperties in client.GetPropertiesOfKeysAsync())
        {
            yield return keyProperties;
        }
    }

    public async IAsyncEnumerable<SecretProperties> GetVaultAssociatedSecrets(Uri kvUri)
    {
        if (kvUri is not null)
        {
            var client = new SecretClient(kvUri, _authService.Credential);
            await foreach (var secretProperties in client.GetPropertiesOfSecretsAsync())
            {
                yield return secretProperties;
            }
        }
    }

    public async Task<KeyVaultKey> UpdateKey(KeyProperties properties, Uri KeyVaultUri)
    {
        var client = new KeyClient(KeyVaultUri, _authService.Credential);
        return await client.UpdateKeyPropertiesAsync(properties);
    }

    public async Task<SecretProperties> UpdateSecret(SecretProperties properties, Uri KeyVaultUri)
    {
        var client = new SecretClient(KeyVaultUri, _authService.Credential);
        return await client.UpdateSecretPropertiesAsync(properties);
    }

    /// <summary>
    /// Gets the user's current public IP address.
    /// </summary>
    public static async Task<string> GetPublicIpAsync()
    {
        using var http = new System.Net.Http.HttpClient();
        http.Timeout = TimeSpan.FromSeconds(10);
        var ip = await http.GetStringAsync("https://api.ipify.org");
        return ip.Trim();
    }

    /// <summary>
    /// Adds the user's current public IP to the Key Vault firewall rules.
    /// </summary>
    public async Task<(bool Success, string Message)> WhitelistMyIpOnVault(KeyVaultResource vault)
    {
        try
        {
            var ip = await GetPublicIpAsync();
            var vaultName = vault.Data.Name;

            // Use az CLI to add the IP rule
            var (exitCode, output) = await AuthService.RunAzCommandAsync(
                $"keyvault network-rule add --name {vaultName} --ip-address {ip}/32 --output json");

            if (exitCode == 0)
                return (true, $"Added {ip} to {vaultName} firewall rules");

            // Check stderr for details
            var (_, errorOutput) = await AuthService.RunAzCommandAsync(
                $"keyvault network-rule add --name {vaultName} --ip-address {ip}/32 --output json 2>&1");
            return (false, $"Failed to add IP to firewall: {(string.IsNullOrEmpty(errorOutput) ? output : errorOutput)}");
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Grants the current user "Key Vault Secrets User" + "Key Vault Reader" roles on the vault.
    /// </summary>
    public async Task<(bool Success, string Message)> GrantMyselfAccessToVault(KeyVaultResource vault)
    {
        try
        {
            var vaultResourceId = vault.Data.Id.ToString();
            var userEmail = _authService.AuthenticatedUserClaims?.Email;
            if (string.IsNullOrEmpty(userEmail))
                return (false, "Not authenticated. Please sign in first.");

            // Get the user's object ID
            var (exitCode, output) = await AuthService.RunAzCommandAsync(
                $"ad signed-in-user show --query id -o tsv");
            if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
                return (false, "Could not determine your Azure AD user ID");

            var userObjectId = output.Trim();
            var results = new System.Text.StringBuilder();
            var anySuccess = false;

            // Assign Key Vault Secrets User role
            var (exitCode1, output1) = await AuthService.RunAzCommandAsync(
                $"role assignment create --assignee {userObjectId} --role \"Key Vault Secrets User\" --scope {vaultResourceId} --output json");
            if (exitCode1 == 0)
            {
                results.AppendLine("Assigned: Key Vault Secrets User");
                anySuccess = true;
            }
            else
            {
                results.AppendLine($"Key Vault Secrets User: {(output1.Contains("already exists") ? "Already assigned" : "Failed - insufficient permissions")}");
                if (output1.Contains("already exists")) anySuccess = true;
            }

            // Assign Key Vault Reader role
            var (exitCode2, output2) = await AuthService.RunAzCommandAsync(
                $"role assignment create --assignee {userObjectId} --role \"Key Vault Reader\" --scope {vaultResourceId} --output json");
            if (exitCode2 == 0)
            {
                results.AppendLine("Assigned: Key Vault Reader");
                anySuccess = true;
            }
            else
            {
                results.AppendLine($"Key Vault Reader: {(output2.Contains("already exists") ? "Already assigned" : "Failed - insufficient permissions")}");
                if (output2.Contains("already exists")) anySuccess = true;
            }

            if (anySuccess)
                results.AppendLine("Note: RBAC changes may take 5-10 minutes to propagate");

            return (anySuccess, results.ToString().Trim());
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}");
        }
    }
}
