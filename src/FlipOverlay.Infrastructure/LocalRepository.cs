using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FlipOverlay.Infrastructure;

public sealed record AppDataPaths(string DataDirectory, bool IsPortable)
{
    public static AppDataPaths Discover(string executableDirectory, string? installedDataRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);
        var root = Path.GetFullPath(executableDirectory);
        if (File.Exists(Path.Combine(root, "portable.flag")))
            return new(Path.Combine(root, "user-data"), true);
        var parent = installedDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(parent)) throw new IOException("No user-scoped data location is available.");
        return new(Path.Combine(Path.GetFullPath(parent), "PoEFlipOverlay"), false);
    }
}

public sealed record StoredManualObservation(string Id, string LeagueId, string GiveCurrency, decimal GiveQuantity,
    string ReceiveCurrency, decimal ReceiveQuantity, decimal? AvailableQuantity, decimal? Gold,
    DateTimeOffset? ObservedAt, DateTimeOffset FetchedAt);

/// <summary>Single writer, serialized operations, rollback journal; no WAL assumption for portable storage.</summary>
public sealed class LocalRepository : IDisposable
{
    private readonly object gate = new();
    private readonly FileStream ownership;
    private readonly SqliteConnection connection;
    private readonly string settingsPath;
    private readonly int maxManualRows;
    private bool disposed;
    public string JournalMode { get; }
    public LocalRepository(string dataDirectory, int maxManualRows = 5000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxManualRows);
        if (maxManualRows > 5000) throw new ArgumentOutOfRangeException(nameof(maxManualRows));
        this.maxManualRows = maxManualRows;
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        settingsPath = Path.Combine(dataDirectory, "settings.json");
        var probe = Path.Combine(dataDirectory, ".write-test-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(probe, ""); File.Delete(probe); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new IOException("The selected data directory is read-only; no AppData fallback was made.", e); }
        ownership = new FileStream(Path.Combine(dataDirectory, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(dataDirectory, "observations.sqlite"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            connection.Open();
            using (var quota = connection.CreateCommand())
            {
                quota.CommandText = "PRAGMA page_size;";
                var pageSize = Convert.ToInt64(quota.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                quota.CommandText = "PRAGMA max_page_count=" + (100L * 1024 * 1024 / pageSize).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";";
                quota.ExecuteScalar();
            }
            using (var journal = connection.CreateCommand())
            {
                journal.CommandText = "PRAGMA journal_mode=DELETE;";
                JournalMode = (string)journal.ExecuteScalar()!;
            }
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            if (version > 1) throw new InvalidDataException("A newer database schema cannot be opened by this version.");
            if (version == 0)
            {
                using var transaction = connection.BeginTransaction();
                using var init = connection.CreateCommand();
                init.Transaction = transaction;
                init.CommandText = "CREATE TABLE IF NOT EXISTS manual_observations (id TEXT PRIMARY KEY, game TEXT NOT NULL, realm TEXT NOT NULL, league_id TEXT NOT NULL, payload TEXT NOT NULL, fetched_at TEXT NOT NULL); CREATE INDEX IF NOT EXISTS manual_league_idx ON manual_observations(game, realm, league_id, fetched_at); CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL); PRAGMA user_version=1;";
                init.ExecuteNonQuery();
                transaction.Commit();
            }
        }
        catch { connection?.Dispose(); ownership.Dispose(); throw; }
    }
    public void SaveManual(StoredManualObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (string.IsNullOrWhiteSpace(observation.Id) || observation.Id.Length > 200 ||
            string.IsNullOrWhiteSpace(observation.LeagueId) || observation.LeagueId.Length > 200 ||
            string.IsNullOrWhiteSpace(observation.GiveCurrency) || observation.GiveCurrency.Length > 200 ||
            string.IsNullOrWhiteSpace(observation.ReceiveCurrency) || observation.ReceiveCurrency.Length > 200 ||
            observation.GiveCurrency == observation.ReceiveCurrency ||
            observation.GiveQuantity <= 0 || observation.GiveQuantity > long.MaxValue ||
            observation.ReceiveQuantity <= 0 || observation.ReceiveQuantity > long.MaxValue ||
            decimal.Truncate(observation.GiveQuantity) != observation.GiveQuantity ||
            decimal.Truncate(observation.ReceiveQuantity) != observation.ReceiveQuantity ||
            observation.Gold is < 0 || observation.AvailableQuantity is < 0)
            throw new ArgumentException("Invalid manual observation.");
        lock (gate)
        {
            EnsureOpen();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO manual_observations(id,game,realm,league_id,payload,fetched_at) VALUES($id,'poe1','pc',$league,$payload,$time);";
            command.Parameters.AddWithValue("$id", observation.Id);
            command.Parameters.AddWithValue("$league", observation.LeagueId);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(observation));
            command.Parameters.AddWithValue("$time", observation.FetchedAt.ToUniversalTime().ToString("O"));
            command.ExecuteNonQuery();
            using var cleanup = connection.CreateCommand();
            cleanup.Transaction = transaction;
            cleanup.CommandText = "DELETE FROM manual_observations WHERE id NOT IN (SELECT id FROM manual_observations ORDER BY fetched_at DESC, id DESC LIMIT $limit);";
            cleanup.Parameters.AddWithValue("$limit", maxManualRows);
            cleanup.ExecuteNonQuery();
            transaction.Commit();
        }
    }
    public IReadOnlyList<StoredManualObservation> LoadManual(string leagueId)
    {
        lock (gate)
        {
            EnsureOpen();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM manual_observations WHERE game='poe1' AND realm='pc' AND league_id=$league ORDER BY fetched_at DESC LIMIT 500;";
            command.Parameters.AddWithValue("$league", leagueId);
            using var reader = command.ExecuteReader();
            var rows = new List<StoredManualObservation>();
            while (reader.Read()) rows.Add(JsonSerializer.Deserialize<StoredManualObservation>(reader.GetString(0)) ?? throw new InvalidDataException("Invalid stored observation."));
            return rows;
        }
    }
    public void SaveSetting(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        if (key.Length > 200 || value.Length > 1024 * 1024) throw new ArgumentException("Setting exceeds the configured size bound.");
        lock (gate)
        {
            EnsureOpen();
            var settings = ReadSettings();
            settings[key] = value;
            var json = JsonSerializer.SerializeToUtf8Bytes(settings);
            if (json.Length > 1024 * 1024) throw new ArgumentException("Settings exceed 1 MiB.");
            var temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(json);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, settingsPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public string? LoadSetting(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (gate)
        {
            EnsureOpen();
            var settings = ReadSettings();
            if (settings.TryGetValue(key, out var value)) return value;
            // Read-only migration fallback for prototypes that stored settings in SQLite.
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key=$key;";
            command.Parameters.AddWithValue("$key", key);
            return command.ExecuteScalar() as string;
        }
    }
    private Dictionary<string, string> ReadSettings()
    {
        if (!File.Exists(settingsPath)) return new(StringComparer.Ordinal);
        if (new FileInfo(settingsPath).Length > 1024 * 1024) throw new InvalidDataException("Settings file exceeds 1 MiB.");
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(settingsPath)) ?? new(StringComparer.Ordinal); }
        catch (JsonException e) { throw new InvalidDataException("Settings JSON is invalid. It was not silently replaced.", e); }
    }
    private void EnsureOpen() { ObjectDisposedException.ThrowIf(disposed, this); }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            connection.Dispose();
            ownership.Dispose();
        }
    }
}
