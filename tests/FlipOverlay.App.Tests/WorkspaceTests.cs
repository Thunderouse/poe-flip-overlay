using Xunit;
namespace FlipOverlay.App.Tests;

public class WorkspaceTests
{
    [Fact]
    public async Task Default_backend_is_empty_and_honestly_unavailable()
    {
        var vm = new WorkspaceViewModel(new UnavailableWorkspaceBackend());
        await vm.InitializeAsync();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(vm.Projection.Rows);
        Assert.Contains("not configured", vm.Status, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Manual_requires_direction_and_explicit_time_or_unknown()
    {
        var draft = new ManualEntryDraft { LeagueId = "test", GiveCurrency = "chaos", ReceiveCurrency = "divine", GiveQuantity = "100", ReceiveQuantity = "1", AvailableQuantity = "2", Gold = "" };
        Assert.Throws<ArgumentException>(() => draft.ToEntry());
        draft.TimestampUnknown = true;
        var entry = draft.ToEntry();
        Assert.Null(entry.ObservedAt);
        Assert.Null(entry.Gold);
        Assert.Equal(100m, entry.GiveQuantity);
        Assert.Equal(1m, entry.ReceiveQuantity);
        Assert.Contains("100 chaos → 1 divine", draft.Confirmation);
    }
}
