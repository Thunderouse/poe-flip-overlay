using System.Text.Json;
using System.Text.Json.Serialization;
using FlipOverlay.Core;

namespace FlipOverlay.App;

public interface IRecipeWorkspaceBackend
{
    Task ImportCatalogAsync(string json, string expectedPatch, bool confirmedInGame, CancellationToken cancellationToken);
}

public sealed record UserRecipe(string Id, string InputId, long InputUnits, string OutputId, long OutputUnits,
    string Npc, string Act, string Condition, string Source, DateTimeOffset CheckedAt);
public sealed record UserRecipeCatalog(string Version, string Game, string Realm, string Patch,
    string SourceId, string SettlementId, IReadOnlyList<UserRecipe> Recipes)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
    public static UserRecipeCatalog Parse(string json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > 256 * 1024)
            throw new ArgumentException("Recipe catalog must be a JSON document under 256 KiB.");
        UserRecipeCatalog catalog;
        try { catalog = JsonSerializer.Deserialize<UserRecipeCatalog>(json, Options) ?? throw new JsonException("Empty catalog."); }
        catch (JsonException e) { throw new ArgumentException("Invalid recipe catalog JSON: " + e.Message, nameof(json), e); }
        bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 300;
        if (catalog.Game != "poe1" || catalog.Realm != "pc" || catalog.SourceId != "poe.ninja" ||
            catalog.SettlementId != "chaos" || !Text(catalog.Patch) || !Text(catalog.Version) ||
            catalog.Recipes is null || catalog.Recipes.Count is < 1 or > 100)
            throw new ArgumentException("Catalog requires PoE1 PC, poe.ninja exact IDs, chaos settlement, version, patch and 1–100 directed recipes.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in catalog.Recipes)
        {
            if (recipe is null || !Text(recipe.Id) || !ids.Add(recipe.Id) || !Text(recipe.InputId) || !Text(recipe.OutputId) ||
                recipe.InputId == recipe.OutputId || recipe.InputUnits <= 0 || recipe.OutputUnits <= 0 ||
                !Text(recipe.Npc) || !Text(recipe.Act) || !Text(recipe.Condition) || !Text(recipe.Source) ||
                recipe.CheckedAt == default || recipe.CheckedAt > now)
                throw new ArgumentException("Every directed recipe needs a unique ID, positive integer ratio, NPC/act/conditions, provenance and known check time.");
        }
        return catalog;
    }
    public IReadOnlyList<RecipeEdge> Edges() => Recipes.Select(r => new RecipeEdge(r.Id, r.InputId, r.OutputId,
        new BatchRate(r.InputUnits, r.OutputUnits), Patch, r.Source, r.CheckedAt, r.Npc, r.Condition,
        Game, Realm, Version, r.Act)).ToArray();
}
