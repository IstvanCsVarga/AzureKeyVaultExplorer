using KeyVaultExplorer.Models;
using static KeyVaultExplorer.ViewModels.VaultPageViewModel;

namespace KeyVaultExplorer.Tests.Unit;

public class FilterHelperTests
{
    private record TestItem(string Name, string ContentType, Dictionary<string, string>? Tags);

    private static List<TestItem> SampleItems =>
    [
        new("account-svc-release-2-kafka-bootstrap", "application/json", new() { { "env", "prod" } }),
        new("db-connection-string-primary", "text/plain", new() { { "env", "staging" } }),
        new("api-key-external-partner", "application/json", null),
        new("certificate-thumbprint-auth", "text/plain", new() { { "file-encoding", "utf-8" } }),
        new("kafka-consumer-group-id", "application/json", new() { { "team", "platform" } }),
    ];

    [Fact]
    public void EmptyQuery_ReturnsAll()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "", i => i.Name, i => i.Tags!, i => i.ContentType);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void NullQuery_ReturnsAll()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, null!, i => i.Name, i => i.Tags!, i => i.ContentType);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void SubstringMatch_CaseInsensitive()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "kafka", i => i.Name, i => i.Tags!, i => i.ContentType);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, i => i.Name.Contains("kafka"));
    }

    [Fact]
    public void SubstringMatch_CaseSensitive_NoMatch()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "Kafka", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: false, caseSensitive: true);
        Assert.Empty(result);
    }

    [Fact]
    public void SubstringMatch_CaseSensitive_Match()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "kafka", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: false, caseSensitive: true);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Regex_SimplePattern()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "kafka.*bootstrap", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: true);
        Assert.Single(result);
        Assert.Equal("account-svc-release-2-kafka-bootstrap", result[0].Name);
    }

    [Fact]
    public void Regex_StartAnchor()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "^api-", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: true);
        Assert.Single(result);
        Assert.Equal("api-key-external-partner", result[0].Name);
    }

    [Fact]
    public void Regex_CharClass()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "release-[0-9]", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: true);
        Assert.Single(result);
    }

    [Fact]
    public void Regex_CaseSensitive()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "^Account", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: true, caseSensitive: true);
        Assert.Empty(result);
    }

    [Fact]
    public void Regex_CaseInsensitive()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "^Account", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: true, caseSensitive: false);
        Assert.Single(result);
    }

    [Fact]
    public void Regex_InvalidPattern_ReturnsEmpty()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "[invalid(", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: true);
        Assert.Empty(result);
    }

    [Fact]
    public void MatchesTags()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "platform", i => i.Name, i => i.Tags!, i => i.ContentType);
        Assert.Single(result);
        Assert.Equal("kafka-consumer-group-id", result[0].Name);
    }

    [Fact]
    public void MatchesContentType()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "text/plain", i => i.Name, i => i.Tags!, i => i.ContentType);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void NullTags_DoesNotThrow()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "external", i => i.Name, i => i.Tags!, i => i.ContentType);
        Assert.Single(result);
        Assert.Equal("api-key-external-partner", result[0].Name);
    }

    [Fact]
    public void Regex_MatchesTags()
    {
        var result = KeyVaultFilterHelper.FilterByQuery(
            SampleItems, "^prod$", i => i.Name, i => i.Tags!, i => i.ContentType,
            isRegex: true);
        Assert.Single(result);
        Assert.Equal("account-svc-release-2-kafka-bootstrap", result[0].Name);
    }
}
