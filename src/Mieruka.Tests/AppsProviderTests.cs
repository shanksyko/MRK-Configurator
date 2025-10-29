using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Mieruka.App.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Mieruka.Tests;

public sealed class AppsProviderTests : IDisposable
{
    private readonly ILogger _originalLogger;

    public AppsProviderTests()
    {
        _originalLogger = Log.Logger;
    }

    public void Dispose()
    {
        Log.Logger = _originalLogger;
        InstalledAppsProvider.ResetTestOverrides();
    }

    [Fact]
    public void ResolveFromStartMenuShortcuts_IgnoresInaccessibleEntries()
    {
        var events = new List<LogEvent>();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(new DelegatingSink(events.Add))
            .CreateLogger();

        InstalledAppsProvider.SetRegistryLocationsForTesting(Array.Empty<(RegistryKey, string, string)>());
        InstalledAppsProvider.SetStartMenuRootsForTesting(new[] { @"C:\\StartMenu" });
        InstalledAppsProvider.SetFileSystemForTesting(new ThrowingFileSystem());

        var shortcut = InstalledAppsProvider.ResolveFromStartMenuShortcutsForTesting("Sample App");

        Assert.Null(shortcut);
        Assert.Contains(
            events,
            log => log.Level == LogEventLevel.Warning
                && log.MessageTemplate.Text.Contains("Failed to enumerate shortcuts", StringComparison.Ordinal));
    }

    private sealed class DelegatingSink : ILogEventSink
    {
        private readonly Action<LogEvent> _callback;

        public DelegatingSink(Action<LogEvent> callback)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public void Emit(LogEvent logEvent) => _callback(logEvent);
    }

    private sealed class ThrowingFileSystem : InstalledAppsProvider.IFileSystemAccessor
    {
        public bool DirectoryExists(string? path) => true;

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions options)
            => throw new UnauthorizedAccessException("Access denied");

        public bool FileExists(string? path) => false;
    }
}
