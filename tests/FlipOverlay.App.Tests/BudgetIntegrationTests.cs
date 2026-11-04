using FlipOverlay.App;
using FlipOverlay.Infrastructure;
using Xunit;
namespace FlipOverlay.App.Tests;

public sealed class BudgetIntegrationTests
{
    [Fact]
    public async Task Large_capital_budget_keeps_a_profitable_low_stock_manual_opportunity()
    {
        var root = Path.Combine(Path.GetTempPath(), "depth-sizing-" + Guid.NewGuid());
        try
        {
            using var repo = new LocalRepository(root);
            using var http = new HttpClient();
            using var backend = new LocalWorkspaceBackend(repo, http, TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            await backend.SubmitManualAsync(new("L", "a", 10, "b", 1, 100, null, now), TestContext.Current.CancellationToken);
            await backend.SubmitManualAsync(new("L", "b", 1, "a", 12, 12, null, now), TestContext.Current.CancellationToken);
            var row = Assert.Single((await backend.RefreshAsync(new("L", WorkspaceMode.Observed, "a", 1000), TestContext.Current.CancellationToken)).Rows);
            Assert.Equal(10m, row.Capital);
            Assert.Equal(2m, row.Profit);
            Assert.Null(row.Gold);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Manual_review_rejects_stock_smaller_than_confirmed_lot()
    {
        var draft = new ManualEntryDraft { LeagueId = "L", GiveCurrency = "a", ReceiveCurrency = "b", GiveQuantity = "10", ReceiveQuantity = "3", AvailableQuantity = "2", TimestampUnknown = true };
        Assert.Throws<ArgumentException>(() => draft.ToEntry());
    }
    [Fact]
    public async Task Large_quantities_are_limited_to_confirmed_stock()
    {
        var root = Path.Combine(Path.GetTempPath(), "overflow-budget-" + Guid.NewGuid());
        try
        {
            using var repo = new LocalRepository(root);
            using var http = new HttpClient();
            using var backend = new LocalWorkspaceBackend(repo, http, TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            await backend.SubmitManualAsync(new("L", "a", 1, "b", long.MaxValue, long.MaxValue, 0, now), TestContext.Current.CancellationToken);
            await backend.SubmitManualAsync(new("L", "b", 1, "a", 2, long.MaxValue, 0, now), TestContext.Current.CancellationToken);
            var row = Assert.Single((await backend.RefreshAsync(new("L", WorkspaceMode.Observed, "a", long.MaxValue), TestContext.Current.CancellationToken)).Rows);
            Assert.Equal(1, row.Capital);
            Assert.Equal(0, row.Gold);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Settings_round_trip_all_budget_fields()
    {
        var root = Path.Combine(Path.GetTempPath(), "settings-budget-" + Guid.NewGuid());
        try
        {
            using var repo = new LocalRepository(root);
            using var http = new HttpClient();
            using var backend = new LocalWorkspaceBackend(repo, http, TimeProvider.System);
            var settings = new WorkspaceSettings(true, .7, "divine", 12, 42, 31, true);
            await backend.SaveSettingsAsync(settings, TestContext.Current.CancellationToken);
            Assert.Equal(settings, await backend.LoadSettingsAsync(TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Both_profitable_entry_currencies_survive_and_whole_lot_gold_is_counted()
    {
        var root = Path.Combine(Path.GetTempPath(), "settlement-budget-" + Guid.NewGuid());
        try
        {
            using var repo = new LocalRepository(root);
            using var http = new HttpClient();
            using var backend = new LocalWorkspaceBackend(repo, http, TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            await backend.SubmitManualAsync(new("L", "a", 10, "b", 3, 30, 7, now), TestContext.Current.CancellationToken);
            await backend.SubmitManualAsync(new("L", "b", 2, "a", 8, 80, 11, now), TestContext.Current.CancellationToken);
            var rows = (await backend.RefreshAsync(new("L", WorkspaceMode.Observed, CapitalBudget: 20), TestContext.Current.CancellationToken)).Rows;
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, x => x.Unit == "a"); Assert.Contains(rows, x => x.Unit == "b");
            var a = Assert.Single(rows, x => x.Unit == "a");
            Assert.Equal(47, a.Gold); // two a lots + three b lots; no fractional fees.
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    public void Manual_integer_fields_reject_fraction_or_overflow(string value)
    {
        var draft = new ManualEntryDraft { LeagueId = "L", GiveCurrency = "a", ReceiveCurrency = "b", GiveQuantity = value, ReceiveQuantity = "1", TimestampUnknown = true };
        Assert.Throws<ArgumentException>(() => draft.ToEntry());
    }
    [Fact]
    public async Task Budget_applies_to_selected_settlement_and_unknown_gold_is_excluded()
    {
        var root = Path.Combine(Path.GetTempPath(), "budget-" + Guid.NewGuid());
        try
        {
            using var repo = new LocalRepository(root);
            using var http = new HttpClient();
            using var backend = new LocalWorkspaceBackend(repo, http, TimeProvider.System);
            var now = DateTimeOffset.UtcNow;
            await backend.SubmitManualAsync(new("L", "a", 10, "b", 3, 3, null, now), TestContext.Current.CancellationToken);
            await backend.SubmitManualAsync(new("L", "b", 1, "a", 6, 12, 5, now), TestContext.Current.CancellationToken);
            var snapshot = await backend.RefreshAsync(new("L", WorkspaceMode.Observed, "a", 9), TestContext.Current.CancellationToken);
            Assert.Empty(snapshot.Rows);
            snapshot = await backend.RefreshAsync(new("L", WorkspaceMode.Observed, "b", 2), TestContext.Current.CancellationToken);
            Assert.Single(snapshot.Rows);
            snapshot = await backend.RefreshAsync(new("L", WorkspaceMode.Observed, "b", 2, 100), TestContext.Current.CancellationToken);
            Assert.Empty(snapshot.Rows);
        }
        finally { Directory.Delete(root, true); }
    }
}
