using FlipOverlay.Infrastructure;
using Xunit;

namespace FlipOverlay.Infrastructure.Tests;

public sealed class LocalRepositoryTests
{
    [Fact]
    public void Portable_flag_uses_only_adjacent_user_data_and_second_writer_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "flip test Юникод " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "portable.flag"), "");
        try
        {
            var paths = AppDataPaths.Discover(root);
            Assert.True(paths.IsPortable);
            Assert.Equal(Path.Combine(root, "user-data"), paths.DataDirectory);
            using var first = new LocalRepository(paths.DataDirectory);
            Assert.Throws<IOException>(() => new LocalRepository(paths.DataDirectory));
            first.SaveManual(new StoredManualObservation("a", "Allflame", "chaos", 10, "divine", 1, 2, null,
                DateTimeOffset.Parse("2026-11-04T10:00:00Z"), DateTimeOffset.Parse("2026-11-04T10:01:00Z")));
            Assert.Single(first.LoadManual("Allflame"));
            Assert.Empty(first.LoadManual("Standard"));
            Assert.Equal("delete", first.JournalMode.ToLowerInvariant());
            first.SaveSetting("view", "List");
            Assert.Equal("List", first.LoadSetting("view"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void Manual_history_is_bounded_and_oldest_rows_are_pruned()
    {
        var root = Path.Combine(Path.GetTempPath(), "flip-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var repo = new LocalRepository(root, maxManualRows: 3);
            var start = DateTimeOffset.Parse("2026-11-04T10:00:00Z");
            for (var i = 0; i < 6; i++)
                repo.SaveManual(new(i.ToString(), "L", "A", 1, "B", 2, null, null, start, start.AddSeconds(i)));
            Assert.Equal(new[] { "5", "4", "3" }, repo.LoadManual("L").Select(x => x.Id));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Settings_are_atomic_bounded_json_and_survive_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "flip-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var repo = new LocalRepository(root))
            {
                repo.SaveSetting("mode", "List");
                Assert.True(File.Exists(Path.Combine(root, "settings.json")));
                Assert.Contains("List", File.ReadAllText(Path.Combine(root, "settings.json")));
                Assert.Throws<ArgumentException>(() => repo.SaveSetting("oversized", new string('x', 1024 * 1024 + 1)));
            }
            using var reopened = new LocalRepository(root);
            Assert.Equal("List", reopened.LoadSetting("mode"));
            Assert.DoesNotContain(Directory.GetFiles(root), name => name.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1.5, 1)]
    [InlineData(1, 0)]
    public void Invalid_stored_manual_quantities_are_rejected(double give, double receive)
    {
        var root = Path.Combine(Path.GetTempPath(), "flip-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var repo = new LocalRepository(root);
            Assert.Throws<ArgumentException>(() => repo.SaveManual(new("a", "L", "A", (decimal)give, "B", (decimal)receive, null, null, null, DateTimeOffset.UtcNow)));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void Installed_mode_uses_explicit_user_scoped_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "flip-installed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = AppDataPaths.Discover(root, Path.Combine(root, "my-local-app-data"));
            Assert.False(paths.IsPortable);
            Assert.Equal(Path.Combine(root, "my-local-app-data", "PoEFlipOverlay"), paths.DataDirectory);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
