using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Windowing;
using KeyVaultExplorer.Exceptions;
using KeyVaultExplorer.Models;
using KeyVaultExplorer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

//#if WINDOWS
//using Windows.Data.Xml.Dom;
//using Windows.UI.Notifications;
//#endif

namespace KeyVaultExplorer.ViewModels;

public partial class VaultPageViewModel : ViewModelBase
{
    private readonly AuthService _authService;

    private readonly ClipboardService _clipboardService;

    private readonly VaultService _vaultService;

    private NotificationViewModel _notificationViewModel;

    private SettingsPageViewModel _settingsPageViewModel;
    public string VaultTotalString => VaultContents.Count == 0 || VaultContents.Count > 1 ? $"{VaultContents.Count} items" : "1 item";

    [ObservableProperty]
    private string authorizationMessage;

    [ObservableProperty]
    private bool hasAuthorizationError = false;

    [ObservableProperty]
    private bool isRbacError = false;

    [ObservableProperty]
    private bool isNetworkError = false;

    // Vault resource ID extracted from the 403 error for fix actions
    private string _errorVaultResourceId;
    private string _errorVaultName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VaultTotalString))]
    private bool isBusy = false;

    [ObservableProperty]
    private string searchQuery;

    [ObservableProperty]
    private bool isRegexEnabled = false;

    [ObservableProperty]
    private bool isCaseSensitive = false;

    [ObservableProperty]
    private KeyVaultContentsAmalgamation selectedRow;

    [ObservableProperty]
    private TabStripItem selectedTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VaultTotalString))]
    private ObservableCollection<KeyVaultContentsAmalgamation> vaultContents;

    [ObservableProperty]
    private Uri vaultUri;

    private readonly Lazy<Bitmap> BitmapImage;

    public VaultPageViewModel()
    {
        _vaultService = Defaults.Locator.GetRequiredService<VaultService>();
        _authService = Defaults.Locator.GetRequiredService<AuthService>();
        _settingsPageViewModel = Defaults.Locator.GetRequiredService<SettingsPageViewModel>();
        _notificationViewModel = Defaults.Locator.GetRequiredService<NotificationViewModel>();
        _clipboardService = Defaults.Locator.GetRequiredService<ClipboardService>();
        vaultContents = [];
        BitmapImage = new Lazy<Bitmap>(() => LoadImage("avares://KeyVaultExplorer/Assets/AppIcon.ico"));
    }

    public Bitmap LazyLoadedImage => BitmapImage.Value.CreateScaledBitmap(new Avalonia.PixelSize(24, 24), BitmapInterpolationMode.HighQuality);

    private static Bitmap LoadImage(string uri)
    {
        var asset = AssetLoader.Open(new Uri(uri));
        return new Bitmap(asset);
    }

    public Dictionary<KeyVaultItemType, bool> LoadedItemTypes { get; set; } = new() { };
    private IEnumerable<KeyVaultContentsAmalgamation> _vaultContents { get; set; } = [];

    public async Task ClearClipboardAsync()
    {
        await Task.Delay(_settingsPageViewModel.ClearClipboardTimeout * 1000); // convert to seconds
        await _clipboardService.ClearAsync();
    }

    public async Task FilterAndLoadVaultValueType(KeyVaultItemType item)
    {
        try
        {
            HasAuthorizationError = false;

            if (!LoadedItemTypes.ContainsKey(item))
            {
                IsBusy = true;

                switch (item)
                {
                    case KeyVaultItemType.Certificate:
                        await LoadAndMarkAsLoaded(GetCertificatesForVault, KeyVaultItemType.Certificate);
                        break;

                    case KeyVaultItemType.Key:
                        await LoadAndMarkAsLoaded(GetKeysForVault, KeyVaultItemType.Key);
                        break;

                    case KeyVaultItemType.Secret:
                        await LoadAndMarkAsLoaded(GetSecretsForVault, KeyVaultItemType.Secret);
                        break;

                    case KeyVaultItemType.All:
                        VaultContents.Clear();
                        var loadTasks = new List<Task>
                            {
                                LoadAndMarkAsLoaded(GetSecretsForVault, KeyVaultItemType.Secret),
                                LoadAndMarkAsLoaded(GetKeysForVault, KeyVaultItemType.Key),
                                LoadAndMarkAsLoaded(GetCertificatesForVault, KeyVaultItemType.Certificate)
                            };
                        await Task.WhenAny(loadTasks);
                        LoadedItemTypes.TryAdd(KeyVaultItemType.All, true);
                        break;

                    default:
                        break;
                }
            }
        }
        catch (Exception ex) when (ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
        {
            HasAuthorizationError = true;
            var msg = ex.Message;

            // Extract vault name from the error
            var vaultNameMatch = System.Text.RegularExpressions.Regex.Match(msg, @"Vault:\s*([^;\r\n]+)");
            _errorVaultName = vaultNameMatch.Success ? vaultNameMatch.Groups[1].Value.Trim() : null;

            // Extract resource ID
            var resourceMatch = System.Text.RegularExpressions.Regex.Match(msg, @"Resource:\s*'([^']+)'");
            _errorVaultResourceId = resourceMatch.Success ? resourceMatch.Groups[1].Value.Trim() : null;

            if (msg.Contains("ForbiddenByRbac") || msg.Contains("Assignment: (not found)"))
            {
                IsRbacError = true;
                IsNetworkError = false;
                AuthorizationMessage = $"Access denied on {_errorVaultName ?? "this vault"}.\nYou don't have the required RBAC role assignment.";
            }
            else if (msg.Contains("ForbiddenByFirewall") || msg.Contains("PublicNetworkAccess") || msg.Contains("firewall"))
            {
                IsRbacError = false;
                IsNetworkError = true;
                AuthorizationMessage = $"Network access denied on {_errorVaultName ?? "this vault"}.\nYour IP is not whitelisted in the vault's firewall rules.";
            }
            else
            {
                // Unknown 403 -- show both options
                IsRbacError = true;
                IsNetworkError = true;
                AuthorizationMessage = $"Access denied on {_errorVaultName ?? "this vault"}.\nThis could be a permission or network issue.";
            }
        }
        catch { }
        finally
        {
            var contents = item == KeyVaultItemType.All ? _vaultContents : _vaultContents.Where(x => item == x.Type);

            VaultContents = KeyVaultFilterHelper.FilterByQuery(contents, SearchQuery, item => item.Name, item => item.Tags, item => item.ContentType, IsRegexEnabled, IsCaseSensitive);

            await DelaySetIsBusy(false);
        }
    }

    public async Task GetCertificatesForVault(Uri kvUri)
    {
        var certs = _vaultService.GetVaultAssociatedCertificates(kvUri);
        await foreach (var val in certs)
        {
            VaultContents.Add(new KeyVaultContentsAmalgamation
            {
                Name = val.Name,
                Id = val.Id,
                Type = KeyVaultItemType.Certificate,
                VaultUri = val.VaultUri,
                ValueUri = val.Id,
                Version = val.Version,
                CertificateProperties = val,
                Tags = val.Tags,
                UpdatedOn = val.UpdatedOn,
                CreatedOn = val.CreatedOn,
                ExpiresOn = val.ExpiresOn,
                Enabled = val.Enabled,
                NotBefore = val.NotBefore,
                RecoverableDays = val.RecoverableDays,
                RecoveryLevel = val.RecoveryLevel
            });
        }
        _vaultContents = VaultContents;
    }

    public async Task GetKeysForVault(Uri kvUri)
    {
        var keys = _vaultService.GetVaultAssociatedKeys(kvUri);
        await foreach (var val in keys)
        {
            VaultContents.Add(new KeyVaultContentsAmalgamation
            {
                Name = val.Name,
                Id = val.Id,
                Type = KeyVaultItemType.Key,
                VaultUri = val.VaultUri,
                ValueUri = val.Id,
                Version = val.Version,
                KeyProperties = val,
                Tags = val.Tags,
                UpdatedOn = val.UpdatedOn,
                CreatedOn = val.CreatedOn,
                ExpiresOn = val.ExpiresOn,
                Enabled = val.Enabled,
                NotBefore = val.NotBefore,
                RecoverableDays = val.RecoverableDays,
                RecoveryLevel = val.RecoveryLevel
            });
        }
        _vaultContents = VaultContents;
    }

    public async Task GetSecretsForVault(Uri kvUri)
    {
        var values = _vaultService.GetVaultAssociatedSecrets(kvUri);
        await foreach (var val in values)
        {
            VaultContents.Add(new KeyVaultContentsAmalgamation
            {
                Name = val.Name,
                Id = val.Id,
                Type = KeyVaultItemType.Secret,
                ContentType = val.ContentType,
                VaultUri = val.VaultUri,
                ValueUri = val.Id,
                Version = val.Version,
                SecretProperties = val,
                Tags = val.Tags,
                UpdatedOn = val.UpdatedOn,
                CreatedOn = val.CreatedOn,
                ExpiresOn = val.ExpiresOn,
                Enabled = val.Enabled,
                NotBefore = val.NotBefore,
                RecoverableDays = val.RecoverableDays,
                RecoveryLevel = val.RecoveryLevel
            });
        }

        _vaultContents = VaultContents;
    }

    [RelayCommand]
    private void CloseError()
    {
        HasAuthorizationError = false;
        IsRbacError = false;
        IsNetworkError = false;
    }

    [RelayCommand]
    private async Task GrantAccess()
    {
        if (string.IsNullOrEmpty(_errorVaultName))
        {
            _notificationViewModel.ShowPopup(new Avalonia.Controls.Notifications.Notification { Title = "Error", Message = "Could not determine vault name" });
            return;
        }

        AuthorizationMessage = $"Granting access to {_errorVaultName}...";

        // Get the vault resource via az CLI
        var (exitCode, output) = await AuthService.RunAzCommandAsync($"ad signed-in-user show --query id -o tsv");
        if (exitCode != 0)
        {
            AuthorizationMessage = "Could not determine your Azure AD user ID. Please sign in first.";
            return;
        }
        var userObjectId = output.Trim();
        var scope = _errorVaultResourceId ?? $"/subscriptions/*/resourceGroups/*/providers/Microsoft.KeyVault/vaults/{_errorVaultName}";

        var results = new System.Text.StringBuilder();
        var anySuccess = false;

        // Key Vault Secrets User
        var (rc1, out1) = await AuthService.RunAzCommandAsync($"role assignment create --assignee {userObjectId} --role \"Key Vault Secrets User\" --scope {scope} --output json");
        if (rc1 == 0)
        { results.AppendLine("Assigned: Key Vault Secrets User"); anySuccess = true; }
        else
        { results.AppendLine(out1.Contains("already exists") ? "Key Vault Secrets User: Already assigned" : "Key Vault Secrets User: Failed (insufficient permissions)"); if (out1.Contains("already exists")) anySuccess = true; }

        // Key Vault Reader
        var (rc2, out2) = await AuthService.RunAzCommandAsync($"role assignment create --assignee {userObjectId} --role \"Key Vault Reader\" --scope {scope} --output json");
        if (rc2 == 0)
        { results.AppendLine("Assigned: Key Vault Reader"); anySuccess = true; }
        else
        { results.AppendLine(out2.Contains("already exists") ? "Key Vault Reader: Already assigned" : "Key Vault Reader: Failed (insufficient permissions)"); if (out2.Contains("already exists")) anySuccess = true; }

        if (anySuccess)
            results.AppendLine("\nRBAC changes may take 5-10 minutes to propagate.");

        AuthorizationMessage = results.ToString().Trim();
        if (anySuccess)
        {
            IsRbacError = false;
            _notificationViewModel.AddMessage(new Avalonia.Controls.Notifications.Notification { Title = "Access Granted", Message = $"Roles assigned on {_errorVaultName}. Retry in a few minutes.", Type = Avalonia.Controls.Notifications.NotificationType.Success });
        }
    }

    [RelayCommand]
    private async Task WhitelistIp()
    {
        if (string.IsNullOrEmpty(_errorVaultName))
        {
            _notificationViewModel.ShowPopup(new Avalonia.Controls.Notifications.Notification { Title = "Error", Message = "Could not determine vault name" });
            return;
        }

        AuthorizationMessage = $"Adding your IP to {_errorVaultName} firewall...";

        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = System.TimeSpan.FromSeconds(10) };
            var ip = (await http.GetStringAsync("https://api.ipify.org")).Trim();

            var (exitCode, output) = await AuthService.RunAzCommandAsync($"keyvault network-rule add --name {_errorVaultName} --ip-address {ip}/32 --output json");

            if (exitCode == 0)
            {
                AuthorizationMessage = $"Added {ip} to {_errorVaultName} firewall. Retry loading the vault.";
                IsNetworkError = false;
                _notificationViewModel.AddMessage(new Avalonia.Controls.Notifications.Notification { Title = "IP Whitelisted", Message = $"{ip} added to {_errorVaultName}", Type = Avalonia.Controls.Notifications.NotificationType.Success });
            }
            else
            {
                AuthorizationMessage = $"Failed to add IP to firewall: {output}";
            }
        }
        catch (System.Exception ex)
        {
            AuthorizationMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Copy(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;

        try
        {
            string value = string.Empty;
            //_ = keyVaultItem.Type switch
            //{
            //    KeyVaultItemType.Key => value = (await _vaultService.GetKey(keyVaultItem.VaultUri, keyVaultItem.Name)).Key.ToRSA().ToXmlString(true),
            //    KeyVaultItemType.Secret => value = (await _vaultService.GetSecret(keyVaultItem.VaultUri, keyVaultItem.Name)).Value,
            //    KeyVaultItemType.Certificate => value = (await _vaultService.GetCertificate(keyVaultItem.VaultUri, keyVaultItem.Name)).Name,
            //    _ => throw new NotImplementedException()
            //};

            if (keyVaultItem.Type == KeyVaultItemType.Key)
            {
                var key = await _vaultService.GetKey(keyVaultItem.VaultUri, keyVaultItem.Name);
                if (key.KeyType == KeyType.Rsa)
                {
                    using var rsa = key.Key.ToRSA();
                    var publicKey = rsa.ExportRSAPublicKey();
                    string pem = "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(publicKey) + "\n-----END PUBLIC KEY-----";
                    value = pem;
                }
            }

            if (keyVaultItem.Type == KeyVaultItemType.Secret)
            {
                var sv = await _vaultService.GetSecret(keyVaultItem.VaultUri, keyVaultItem.Name);
                value = sv.Value;
            }
            if (keyVaultItem.Type == KeyVaultItemType.Certificate)
            {
                var certValue = await _vaultService.GetCertificate(keyVaultItem.VaultUri, keyVaultItem.Name);
            }

            // TODO: figure out why set data object async fails here.
            var dataObject = new DataObject();
            dataObject.Set(DataFormats.Text, value);
            await _clipboardService.SetTextAsync(value);
            ShowInAppNotification("Copied", $"The value of '{keyVaultItem.Name}' has been copied to the clipboard.", NotificationType.Success);
            _ = Task.Run(async () => await ClearClipboardAsync().ConfigureAwait(false));

        }
        catch (KeyVaultItemNotFoundException ex)
        {
            ShowInAppNotification($"A value was not found for '{keyVaultItem.Name}'", $"The value of was not able to be retrieved.\n {ex.Message}", NotificationType.Error);
        }
        catch (KeyVaultInsufficientPrivilegesException ex)
        {
            ShowInAppNotification($"Insufficient Privileges to access '{keyVaultItem.Name}'.", $"The value of was not able to be retrieved.\n {ex.Message}", NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowInAppNotification($"There was an error attempting to access '{keyVaultItem.Name}'.", $"The value of was not able to be retrieved.\n {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CopyUri(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        await _clipboardService.SetTextAsync(keyVaultItem.Id.ToString());
    }

    private async Task DelaySetIsBusy(bool val)
    {
        await Task.Delay(1000);
        IsBusy = val;
    }

    private async Task LoadAndMarkAsLoaded(Func<Uri, Task> loadFunction, KeyVaultItemType type)
    {
        await loadFunction(VaultUri);
        LoadedItemTypes.TryAdd(type, true);
    }

    partial void OnIsRegexEnabledChanged(bool value) => OnSearchQueryChanged(SearchQuery);
    partial void OnIsCaseSensitiveChanged(bool value) => OnSearchQueryChanged(SearchQuery);

    partial void OnSearchQueryChanged(string value)
    {
        var isValidEnum = Enum.TryParse(SelectedTab?.Name.ToString(), true, out KeyVaultItemType parsedEnumValue) && Enum.IsDefined(typeof(KeyVaultItemType), parsedEnumValue);
        var item = isValidEnum ? parsedEnumValue : KeyVaultItemType.Secret;
        string? query = value?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            var contents = _vaultContents;
            if (item != KeyVaultItemType.All)
            {
                contents = contents.Where(k => k.Type == item);
            }
            VaultContents = new ObservableCollection<KeyVaultContentsAmalgamation>(contents);
            return;
        }

        VaultContents = KeyVaultFilterHelper.FilterByQuery(item != KeyVaultItemType.All ? _vaultContents.Where(k => k.Type == item) : _vaultContents, value ?? SearchQuery, item => item.Name, item => item.Tags, item => item.ContentType, IsRegexEnabled, IsCaseSensitive);
    }

    [RelayCommand]
    private void OpenInAzure(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        var uri = $"https://portal.azure.com/#@{_authService.TenantName}/asset/Microsoft_Azure_KeyVault/{keyVaultItem.Type}/{keyVaultItem.Id}";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var isValidEnum = Enum.TryParse(SelectedTab?.Name, true, out KeyVaultItemType parsedEnumValue) && Enum.IsDefined(typeof(KeyVaultItemType), parsedEnumValue);
        var item = isValidEnum ? parsedEnumValue : KeyVaultItemType.Secret;
        LoadedItemTypes.Remove(item);
        if (item.HasFlag(KeyVaultItemType.All))
            _vaultContents = [];

        VaultContents = KeyVaultFilterHelper.FilterByQuery(_vaultContents.Where(v => v.Type != item), SearchQuery, item => item.Name, item => item.Tags, item => item.ContentType, IsRegexEnabled, IsCaseSensitive);

        await FilterAndLoadVaultValueType(item);
    }

    private void ShowInAppNotification(string subject, string message, NotificationType notificationType)
    {
        //TODO: https://github.com/pr8x/DesktopNotifications/issues/26
        var notif = new Avalonia.Controls.Notifications.Notification(subject, message, notificationType);
        _notificationViewModel.AddMessage(notif);

        //#if WINDOWS
        //        var appUserModelId = System.AppDomain.CurrentDomain.FriendlyName;
        //        var toastNotifier = Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(appUserModelId);
        //        var id = new Random().Next(0, 100);
        //        string toastXml = $"""
        //          <toast activationType="protocol"> // protocol,Background,Foreground
        //            <visual>
        //                <binding template='ToastGeneric'><text id="{id}">{message}</text></binding>
        //            </visual>
        //        </toast>
        //        """;
        //        XmlDocument doc = new XmlDocument();
        //        doc.LoadXml(toastXml);
        //        var toast = new ToastNotification(doc)
        //        {
        //            ExpirationTime = DateTimeOffset.Now.AddSeconds(1),
        //            //Tag = "Copied KV Values",
        //            ExpiresOnReboot = true
        //        };
        //        toastNotifier.Show(toast);
        //#endif
    }

    [RelayCommand]
    private void ShowProperties(KeyVaultContentsAmalgamation model)
    {
        if (model == null) return;

        var taskDialog = new AppWindow
        {
            Title = $"{model.Type} {model.Name} Properties",
            Icon = LazyLoadedImage,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowAsDialog = false,
            CanResize = true,
            Content = new PropertiesPage { DataContext = new PropertiesPageViewModel(model) },
            Width = 820,
            Height = 680,
            ExtendClientAreaToDecorationsHint = false,
            // TransparencyLevelHint = new List<WindowTransparencyLevel>() { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur },
            // Background = null,
        };

        var topLevel = Avalonia.Application.Current.GetTopLevel() as AppWindow;
        taskDialog.Show(topLevel);
    }

    public static class KeyVaultFilterHelper
    {
        public static ObservableCollection<T> FilterByQuery<T>(
            IEnumerable<T> source,
            string query,
            Func<T, string> nameSelector,
            Func<T, IDictionary<string, string>> tagsSelector,
            Func<T, string> contentTypeSelector,
            bool isRegex = false,
            bool caseSensitive = false)
        {
            if (string.IsNullOrEmpty(query))
            {
                return new ObservableCollection<T>(source);
            }

            if (isRegex)
            {
                try
                {
                    var regexOptions = caseSensitive
                        ? System.Text.RegularExpressions.RegexOptions.None
                        : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                    var regex = new System.Text.RegularExpressions.Regex(query, regexOptions, TimeSpan.FromSeconds(1));

                    var filteredItems = source.Where(item =>
                        regex.IsMatch(nameSelector(item) ?? "")
                        || regex.IsMatch(contentTypeSelector(item) ?? "")
                        || (tagsSelector(item)?.Any(tag =>
                            regex.IsMatch(tag.Key ?? "") || regex.IsMatch(tag.Value ?? "")) ?? false));

                    return new ObservableCollection<T>(filteredItems);
                }
                catch (System.Text.RegularExpressions.RegexParseException)
                {
                    // Invalid regex -- return empty until user fixes it
                    return new ObservableCollection<T>();
                }
            }

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            // Fuzzy search: split by spaces, all terms must match somewhere in the item
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return new ObservableCollection<T>(source);

            var filtered = source.Where(item =>
            {
                var name = nameSelector(item) ?? "";
                var contentType = contentTypeSelector(item) ?? "";
                var tags = tagsSelector(item);
                var tagText = tags is not null
                    ? string.Join(" ", tags.Select(t => $"{t.Key} {t.Value}"))
                    : "";
                var allText = $"{name} {contentType} {tagText}";

                // Every term must match: exact substring OR fuzzy (within edit distance)
                return terms.All(term =>
                    allText.Contains(term, comparison) || FuzzyContains(allText, term, caseSensitive));
            });

            return new ObservableCollection<T>(filtered);
        }

        /// <summary>
        /// Checks if any segment of the text fuzzy-matches the term (Levenshtein distance).
        /// Splits text by common delimiters and checks each word.
        /// </summary>
        private static bool FuzzyContains(string text, string term, bool caseSensitive)
        {
            if (term.Length < 3) return false; // too short for fuzzy, exact only

            var maxDistance = term.Length <= 4 ? 1 : 2; // allow 1 typo for short terms, 2 for longer
            var words = text.Split(['-', '_', '.', ' ', '/', ':'], StringSplitOptions.RemoveEmptyEntries);

            var compareTerm = caseSensitive ? term : term.ToLowerInvariant();

            foreach (var word in words)
            {
                var compareWord = caseSensitive ? word : word.ToLowerInvariant();
                if (LevenshteinDistance(compareWord, compareTerm) <= maxDistance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Computes Levenshtein edit distance between two strings.
        /// </summary>
        private static int LevenshteinDistance(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            // Early exit if length difference exceeds possible match
            if (Math.Abs(a.Length - b.Length) > 2) return Math.Abs(a.Length - b.Length);

            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];

            for (var j = 0; j <= b.Length; j++)
                prev[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}