using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
namespace FlipOverlay.App;

public sealed partial class ManualEntryDraft : ObservableObject
{
    [ObservableProperty] private string leagueId = "";
    [ObservableProperty] private string giveCurrency = "";
    [ObservableProperty] private string receiveCurrency = "";
    [ObservableProperty] private string giveQuantity = "";
    [ObservableProperty] private string receiveQuantity = "";
    [ObservableProperty] private string availableQuantity = "";
    [ObservableProperty] private string gold = "";
    [ObservableProperty] private string timestamp = "";
    [ObservableProperty] private bool timestampUnknown;
    public ManualEntry ToEntry()
    {
        if (string.IsNullOrWhiteSpace(LeagueId) || string.IsNullOrWhiteSpace(GiveCurrency) || string.IsNullOrWhiteSpace(ReceiveCurrency) || GiveCurrency.Trim().Equals(ReceiveCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter league and two different currency IDs.");
        DateTimeOffset? observed = null;
        if (!TimestampUnknown)
        {
            if (!(Timestamp.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(Timestamp, @"[+-]\d{2}:\d{2}$")) || !DateTimeOffset.TryParse(Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new ArgumentException("Enter an ISO timestamp with timezone, or explicitly select Timestamp unknown.");
            observed = time;
        }
        var give = Number(GiveQuantity, false)!.Value;
        var receive = Number(ReceiveQuantity, false)!.Value;
        var stock = Number(AvailableQuantity, true);
        if (stock < receive) throw new ArgumentException("Available receive quantity cannot be smaller than the confirmed lot.");
        return new(LeagueId.Trim(), GiveCurrency.Trim(), give, ReceiveCurrency.Trim(), receive, stock, Number(Gold, true, true), observed);
    }
    private static decimal? Number(string text, bool optional, bool zero = false)
    {
        if (optional && string.IsNullOrWhiteSpace(text)) return null;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || (zero ? n < 0 : n <= 0)) throw new ArgumentException("Quantities must be positive whole integers up to Int64.MaxValue; gold must be a non-negative integer. Blank optional fields mean Unknown.");
        return n;
    }
    public string Confirmation
    {
        get
        {
            var e = ToEntry();
            return $"League {e.LeagueId}: give {e.GiveQuantity} {e.GiveCurrency} → {e.ReceiveQuantity} {e.ReceiveCurrency}. Available receive quantity: {e.AvailableQuantity?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"}. Gold per exact whole lot: {e.Gold?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"}. Observed: {e.ObservedAt?.ToString("O") ?? "Unknown — not a verified fresh quote"}. Only whole lots within entered stock are evaluated; verify this direction and gold per lot in game.";
        }
    }
}
