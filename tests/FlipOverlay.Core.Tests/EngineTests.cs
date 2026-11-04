using FlipOverlay.Core;
using Xunit;

namespace FlipOverlay.Core.Tests;

public class EngineTests
{
    [Fact]
    public void Ratio_executes_only_whole_batches()
    {
        var fill = new BatchRate(3, 2).Fill(11);
        Assert.Equal(3, fill.Batches);
        Assert.Equal(9, fill.Input);
        Assert.Equal(6, fill.Output);
        Assert.Equal(2, fill.Remainder);
    }
}
