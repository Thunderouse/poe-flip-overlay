using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlipOverlay.Core;

/// <summary>Offline catalog. Parsing evidence never verifies or activates a patch by itself.</summary>
public sealed class JsonRecipeCatalog : IRecipeCatalog
{
    private readonly string game, realm, patch, version;
    private readonly IReadOnlyList<RecipeEdge> recipes;

    private JsonRecipeCatalog(CatalogDocument document, string? verifiedCurrentPatch)
    {
        game = document.Game;
        realm = document.Realm;
        patch = document.Patch;
        version = document.CatalogVersion;
        recipes = verifiedCurrentPatch == patch
            ? Array.AsReadOnly(document.Recipes.Select(r => new RecipeEdge(r.Id, r.InputId, r.OutputId,
                new(r.InputUnits, r.OutputUnits), patch, r.Source, r.CheckedAt, r.Npc, r.Condition,
                game, realm, version, r.Act)).ToArray()) : Array.Empty<RecipeEdge>();
    }

    public IReadOnlyList<RecipeEdge> GetRecipes(MarketContext context) =>
        context.Game == game && context.Realm == realm && context.Patch == patch && context.CatalogVersion == version
            ? recipes : Array.Empty<RecipeEdge>();

    public static JsonRecipeCatalog Load(string json, DateTimeOffset now, string? verifiedCurrentPatch = null)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new JsonException("Catalog must be an object.");
        if (document.SchemaVersion != 1 || !Metadata.Known(document.Patch) ||
            !Metadata.Text(document.Game, document.Realm, document.CatalogVersion) || document.Recipes is null)
            throw new JsonException("Unsupported catalog version or missing scope.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in document.Recipes)
        {
            if (r is null || !Metadata.Text(r.Id, r.InputId, r.OutputId, r.Source, r.Npc, r.Act, r.Condition) ||
                r.Patch != document.Patch || r.InputId == r.OutputId || r.InputUnits <= 0 || r.OutputUnits <= 0 ||
                r.CheckedAt == default || r.CheckedAt > now || !ids.Add(r.Id))
                throw new JsonException("Invalid, duplicate, unscoped, or future-dated recipe.");
        }
        return new(document, verifiedCurrentPatch);
    }

    private sealed record CatalogDocument(int SchemaVersion, string Game, string Realm, string Patch,
        string CatalogVersion, RecipeDocument[] Recipes);
    private sealed record RecipeDocument(string Id, string InputId, string OutputId, long InputUnits,
        long OutputUnits, string Patch, string Source, DateTimeOffset CheckedAt, string Npc, string Act, string Condition);
}

internal static class Metadata
{
    internal static bool Text(params string?[] values) => values.All(v => !string.IsNullOrWhiteSpace(v));
    internal static bool Known(string? value) => Text(value) &&
        !new[] { "unknown", "unverified", "latest" }.Contains(value, StringComparer.OrdinalIgnoreCase);
    internal static bool Valid(MarketContext c) => Text(c.Game, c.Realm, c.LeagueId, c.CatalogVersion, c.MarketKind) && Known(c.Patch);
}
