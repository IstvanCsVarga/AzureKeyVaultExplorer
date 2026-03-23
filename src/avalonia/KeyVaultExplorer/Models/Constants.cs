using System;
using System.IO;

namespace KeyVaultExplorer.Models;

public record TenantInfo(string TenantId, string DisplayName);

public static class Constants
{
    // database password file name
    public const string EncryptedSecretFileName = "keyvaultexplorerforazure_database_password.txt";

    public const string KeychainSecretName = "keyvaultexplorerforazure_database_password";
    public const string KeychainServiceName = "keyvaultexplorerforazure";
    public const string ProtectedKeyFileName = "keyvaultexplorerforazure_database_key.bin";
    public const string DeviceFileTokenName = "keyvaultexplorerforazure_database_device-token.txt";

    public static readonly string LocalAppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "\\KeyVaultExplorerForAzure";

    public static readonly string DatabaseFilePath = LocalAppDataFolder + "\\KeyVaultExplorerForAzure.db";

    public static readonly string DatabasePasswordFilePath = Path.Combine(LocalAppDataFolder, EncryptedSecretFileName);

    // Legacy MSAL cache file name - used for cleanup on upgrade
    public const string LegacyMsalCacheFileName = "keyvaultexplorer_msal_cache.txt";
}
