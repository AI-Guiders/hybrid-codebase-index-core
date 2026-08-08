using HybridCodebaseIndex.Core;
using Xunit;

namespace HybridCodebaseIndex.Core.Tests;

public sealed class FtsCamelCaseTests
{
    [Theory]
    [InlineData("PlanBoardLeaf", new[] { "Plan", "Board", "Leaf" })]
    [InlineData("BoardLeaf", new[] { "Board", "Leaf" })]
    [InlineData("XMLHttpRequest", new[] { "XML", "Http", "Request" })]
    [InlineData("getHTTPResponse", new[] { "get", "HTTP", "Response" })]
    [InlineData("plain", new[] { "plain" })]
    [InlineData("snake_case_id", new[] { "snake", "case", "id" })]
    public void SplitIdentifier_cases(string ident, string[] expected)
    {
        Assert.Equal(expected, FtsCamelCase.SplitIdentifier(ident));
    }

    [Fact]
    public void BuildMatchTerm_middle_segment_ors_and()
    {
        var term = FtsCamelCase.BuildMatchTerm("BoardLeaf");
        Assert.Contains("\"BoardLeaf\"*", term, StringComparison.Ordinal);
        Assert.Contains("\"Board\"*", term, StringComparison.Ordinal);
        Assert.Contains("\"Leaf\"*", term, StringComparison.Ordinal);
        Assert.Contains(" OR ", term, StringComparison.Ordinal);
        Assert.Contains(" AND ", term, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMatchQuery_expands_camel()
    {
        var q = SqliteFtsIndex.BuildMatchQuery("BoardLeaf");
        Assert.NotNull(q);
        Assert.Contains("Board\"*", q, StringComparison.Ordinal);
        Assert.Contains("Leaf\"*", q, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandBodyForFts_appends_segments()
    {
        var body = "class PlanBoardLeaf { }";
        var expanded = FtsCamelCase.ExpandBodyForFts(body);
        Assert.Contains("__hci_camel:", expanded, StringComparison.Ordinal);
        Assert.Contains("Plan Board Leaf", expanded, StringComparison.Ordinal);
        Assert.StartsWith(body, expanded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_middle_camel_segment_hits_after_reindex()
    {
        var root = Path.Combine(Path.GetTempPath(), "hci-camel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "PlanBoardLeaf.cs"),
                "namespace Demo; public sealed class PlanBoardLeaf { public void Run() { } }\n");

            var svc = new CodebaseIndexService(".hybrid-codebase-index");
            await svc.FullRebuildAsync(root);

            var (full, fullErr) = await svc.SearchAsync(root, "PlanBoardLeaf", topN: 10);
            Assert.Null(fullErr);
            Assert.True(full.Hits.Count > 0, "full identifier should hit");

            var (mid, midErr) = await svc.SearchAsync(root, "BoardLeaf", topN: 10);
            Assert.Null(midErr);
            Assert.True(mid.Hits.Count > 0, "middle CamelCase segment BoardLeaf should hit PlanBoardLeaf after densify");
            Assert.Contains(mid.Hits, h => h.Path.Contains("PlanBoardLeaf", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }
}
