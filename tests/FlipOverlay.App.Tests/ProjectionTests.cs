using FlipOverlay.App;
using Xunit;
namespace FlipOverlay.App.Tests;

public class ProjectionTests
{
    [Fact]
    public void Numeric_sort_keeps_unknown_last_both_directions()
    {
        var p = new ResultProjection();
        p.Replace([new("a", "A", Profit: 2), new("b", "B", Profit: 10), new("c", "C")]);
        Assert.Equal(new[] { "b", "a", "c" }, p.Rows.Select(r => r.Id));
        p.SetSort(SortMetric.Profit, false);
        Assert.Equal(new[] { "a", "b", "c" }, p.Rows.Select(r => r.Id));
    }
}
