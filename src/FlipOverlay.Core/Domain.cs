namespace FlipOverlay.Core;

public sealed record BatchFill(long Batches, long Input, long Output, long Remainder);
public sealed record BatchRate(long InputUnits, long OutputUnits)
{
    public BatchFill Fill(long available)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(InputUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(OutputUnits);
        ArgumentOutOfRangeException.ThrowIfNegative(available);
        var batches = available / InputUnits;
        return new(batches, checked(batches * InputUnits), checked(batches * OutputUnits), available % InputUnits);
    }
}
