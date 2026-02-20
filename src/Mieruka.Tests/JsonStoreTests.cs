using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.App.Config;
using Xunit;

namespace Mieruka.Tests;

public sealed class JsonStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MierukaTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string TempFile(string name = "test.json") => Path.Combine(_tempDir, name);

    // ───── Load ─────

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_ReturnsNull()
    {
        var store = new JsonStore<TestPayload>(TempFile("missing.json"));

        var result = await store.LoadAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = new JsonStore<TestPayload>(TempFile());
        var payload = new TestPayload { Name = "Mieruka", Value = 42 };

        await store.SaveAsync(payload);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("Mieruka", loaded!.Name);
        Assert.Equal(42, loaded.Value);
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        var path = TempFile();
        var store = new JsonStore<TestPayload>(path);

        await store.SaveAsync(new TestPayload { Name = "v1", Value = 1 });
        await store.SaveAsync(new TestPayload { Name = "v2", Value = 2 });

        var loaded = await store.LoadAsync();
        Assert.Equal("v2", loaded!.Name);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfMissing()
    {
        var deepPath = Path.Combine(_tempDir, "sub", "dir", "config.json");
        var store = new JsonStore<TestPayload>(deepPath);

        await store.SaveAsync(new TestPayload { Name = "deep" });

        Assert.True(File.Exists(deepPath));
    }

    // ───── Cancellation ─────

    [Fact]
    public async Task SaveAsync_CancellationRequested_Throws()
    {
        var store = new JsonStore<TestPayload>(TempFile());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveAsync(new TestPayload { Name = "data" }, cts.Token));
    }

    [Fact]
    public async Task SaveAsync_NullValue_Throws()
    {
        var store = new JsonStore<TestPayload>(TempFile());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.SaveAsync(null!));
    }

    // ───── Constructor ─────

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new JsonStore<TestPayload>(null!));
    }

    [Fact]
    public void Constructor_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JsonStore<TestPayload>(""));
    }

    [Fact]
    public void FilePath_ReturnsFullPath()
    {
        var store = new JsonStore<TestPayload>(TempFile("config.json"));

        Assert.True(Path.IsPathRooted(store.FilePath));
        Assert.EndsWith("config.json", store.FilePath);
    }

    // ───── Lock timeout ─────

    [Fact]
    public async Task AcquireLock_OrphanedLock_EventuallySucceeds()
    {
        var path = TempFile("locked.json");
        var store = new JsonStore<TestPayload>(path);

        // Create an orphaned lock file (old timestamp)
        var lockPath = path + ".lock";
        await File.WriteAllTextAsync(lockPath, "orphan");
        File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow.AddMinutes(-5));

        // Save should eventually succeed after detecting the orphaned lock
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await store.SaveAsync(new TestPayload { Name = "recovered" }, cts.Token);

        var loaded = await store.LoadAsync();
        Assert.Equal("recovered", loaded!.Name);
    }

    // ───── Helpers ─────

    private sealed class TestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
