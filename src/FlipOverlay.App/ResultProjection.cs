using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlipOverlay.App;

public enum SortMetric { Profit, Roi, Gold, ProfitPerThousandGold, Capital, Age }
public sealed record ResultDto(string Id, string Route, decimal? Profit = null, decimal? Roi = null,
    decimal? Gold = null, decimal? ProfitPerThousandGold = null, decimal? Capital = null,
    decimal? Age = null, string Depth = "Unknown", string Source = "Unknown", string Quality = "Candidate — verify in game",
    string Details = "No execution details supplied.", string Unit = "", DateTimeOffset? ObservedAt = null);

public sealed partial class ResultRow : ObservableObject
{
    [ObservableProperty] private ResultDto data;
    public ResultRow(ResultDto data) => this.data = data;
    public string Id => Data.Id;
    public string Route => Data.Route;
    public string Profit => Format(Data.Profit) + (Data.Unit.Length > 0 ? " " + Data.Unit : "");
    public string Roi => Format(Data.Roi);
    public string Gold => Data.Gold == 0 ? "No gold required" : Format(Data.Gold);
    public string ProfitPerThousandGold => Data.Gold == 0 ? "No gold required" : Format(Data.ProfitPerThousandGold);
    public string Capital => Format(Data.Capital);
    public string Age => Data.Age is null ? "Age unknown" : $"{Data.Age:0}s";
    public string Depth => Data.Depth;
    public string Source => Data.Source + " · " + Data.Quality;
    public string Details => Data.Details;
    public string Summary => $"Net {Profit} · ROI {Roi} · Gold {Gold} · Profit/1k {ProfitPerThousandGold} · Capital {Capital} · Depth {Depth} · {Age} · {Source}";
    private static string Format(decimal? value) => value?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
    partial void OnDataChanged(ResultDto value) => OnPropertyChanged(string.Empty);
}

public sealed partial class ResultProjection : ObservableObject
{
    public ObservableCollection<ResultRow> Rows { get; } = [];
    private readonly Dictionary<string, ResultRow> cache = new(StringComparer.Ordinal);
    private SortMetric metric = SortMetric.Profit;
    private bool descending = true;
    [ObservableProperty] private ResultRow? selected;
    [ObservableProperty] private bool frozen;
    [ObservableProperty] private bool hasPending;
    [ObservableProperty] private bool truncated;
    private ResultDto[]? pending;
    public void Replace(IEnumerable<ResultDto> rows)
    {
        var data = rows.Take(5001).ToArray();
        Truncated = data.Length > 5000;
        data = data.Take(5000).ToArray();
        if (Frozen) { pending = data; HasPending = true; return; }
        var ids = data.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in cache.Keys.Where(x => !ids.Contains(x)).ToArray()) cache.Remove(id);
        foreach (var dto in data)
            if (cache.TryGetValue(dto.Id, out var row)) row.Data = dto;
            else cache.Add(dto.Id, new(dto));
        Reorder();
    }
    public void SetSort(SortMetric sort, bool desc) { metric = sort; descending = desc; if (!Frozen) Reorder(); else HasPending = true; }
    partial void OnFrozenChanged(bool value)
    {
        if (value) return;
        if (pending is { } data) { pending = null; Replace(data); } else Reorder();
        HasPending = false;
    }
    private decimal? Value(ResultRow r) => metric switch
    {
        SortMetric.Profit => r.Data.Profit,
        SortMetric.Roi => r.Data.Roi,
        SortMetric.Gold => r.Data.Gold,
        SortMetric.ProfitPerThousandGold => r.Data.ProfitPerThousandGold,
        SortMetric.Capital => r.Data.Capital,
        _ => r.Data.Age
    };
    private void Reorder()
    {
        var ordered = cache.Values.OrderBy(r => Value(r) is null);
        var target = (descending ? ordered.ThenByDescending(Value) : ordered.ThenBy(Value)).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var keep = target.ToHashSet();
        for (var i = Rows.Count - 1; i >= 0; i--) if (!keep.Contains(Rows[i])) Rows.RemoveAt(i);
        for (var i = 0; i < target.Length; i++)
        {
            if (i < Rows.Count && ReferenceEquals(Rows[i], target[i])) continue;
            var index = Rows.IndexOf(target[i]);
            if (index >= 0) Rows.Move(index, i); else Rows.Insert(i, target[i]);
        }
        if (Selected is not null && !keep.Contains(Selected)) Selected = null;
    }
}
