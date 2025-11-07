using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Forms;
using Mieruka.App.Services.Ui;
using Mieruka.Core.Diagnostics;
using Mieruka.Preview.Capture;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Enrichers;

namespace Mieruka.App;

internal static class Program
{
    internal static Guid SessionId { get; private set; }

    [STAThread]
    private static void Main()
    {
        SessionId = Guid.NewGuid();
        ConfigureLogging(SessionId);

        using var sessionScope = LogContextEx.PushCorrelation(SessionId);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.AddMessageFilter(new MouseMoveCoalescer(16));
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            Log.Information("Iniciando Mieruka Configurator.");

            EnsurePreviewRunsOnGdi();

            var mainForm = new MainForm();
            TabLayoutGuard.Attach(mainForm);
            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "UnhandledException: fluxo principal encerrou abruptamente.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void OnThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "ThreadException");
        }
    }

    private static int _isHandlingUnhandledException;

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (Interlocked.Exchange(ref _isHandlingUnhandledException, 1) != 0)
        {
            return;
        }

        try
        {
            WriteCrashDump();
        }
        catch (Exception dumpException)
        {
            Log.Error(dumpException, "Falha ao gerar mini dump de falha.");
        }

        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "UnhandledException");
        }
        else
        {
            Log.Fatal("UnhandledException: {ExceptionObject}", e.ExceptionObject);
        }
    }

    private static void ConfigureLogging(Guid sessionId)
    {
        var minimumLevel = LogEventLevel.Information;
#if DEBUG
        minimumLevel = LogEventLevel.Debug;
#endif

        var traceOverride = Environment.GetEnvironmentVariable("MIERUKA_TRACE");
        if (!string.IsNullOrWhiteSpace(traceOverride)
            && (string.Equals(traceOverride, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(traceOverride, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(traceOverride, "verbose", StringComparison.OrdinalIgnoreCase)))
        {
            minimumLevel = LogEventLevel.Verbose;
        }

        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "MierukaConfigurator")
            .Enrich.WithProperty("SessionId", sessionId)
            .WriteTo.File(
                path: Path.Combine(logDirectory, "mieruka-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .WriteTo.File(
                formatter: new JsonFormatter(renderMessage: true),
                path: Path.Combine(logDirectory, "mieruka-.json"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .WriteTo.Console();

        Log.Logger = configuration.CreateLogger();
    }

    private static void EnsurePreviewRunsOnGdi()
    {
        try
        {
            if (GraphicsCaptureProvider.DisableGpuGlobally())
            {
                Log.Information("GPU capture globally disabled; forcing GDI preview.");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha ao desabilitar captura GPU globalmente.");
        }

        try
        {
            var options = LoadPreviewGraphicsOptions();
            if (options.Mode != PreviewGraphicsMode.Gdi)
            {
                var sanitized = options with { Mode = PreviewGraphicsMode.Gdi };
                SavePreviewGraphicsOptions(sanitized);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha ao forçar modo GDI nas preferências de preview.");
        }
    }

    private static PreviewGraphicsOptions LoadPreviewGraphicsOptions()
    {
        var store = new JsonStore<PreviewGraphicsOptions>(ResolvePreviewOptionsPath());
        return store.LoadAsync().GetAwaiter().GetResult() ?? new PreviewGraphicsOptions();
    }

    private static void SavePreviewGraphicsOptions(PreviewGraphicsOptions options)
    {
        var store = new JsonStore<PreviewGraphicsOptions>(ResolvePreviewOptionsPath());
        store.SaveAsync(options.Normalize()).GetAwaiter().GetResult();
    }

    private static string ResolvePreviewOptionsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        var directory = Path.Combine(localAppData, "Mieruka", "Configurator");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "preview-options.json");
    }

    private static void WriteCrashDump()
    {
        var crashDirectory = EnsureCrashDirectory();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        var dumpFilePath = Path.Combine(crashDirectory, $"Mieruka_{timestamp}.dmp");

        using var stream = new FileStream(dumpFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var success = NativeMethods.MiniDumpWriteDump(
            GetCurrentProcessHandle(),
            GetCurrentProcessId(),
            stream.SafeFileHandle,
            MinidumpType.MiniDumpWithIndirectlyReferencedMemory,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (!success)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new Win32Exception(errorCode, $"MiniDumpWriteDump falhou com código {errorCode}.");
        }
    }

    private static string EnsureCrashDirectory()
    {
        var baseDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppContext.BaseDirectory,
            Path.GetTempPath()
        };

        foreach (var baseDirectory in baseDirectories)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                continue;
            }

            var directory = Path.Combine(baseDirectory, "Mieruka", "Crashes");

            try
            {
                Directory.CreateDirectory(directory);
                return directory;
            }
            catch
            {
                // Ignorar e tentar próxima opção.
            }
        }

        var fallbackDirectory = Path.Combine(Path.GetTempPath(), "Mieruka", "Crashes");

        try
        {
            Directory.CreateDirectory(fallbackDirectory);
        }
        catch
        {
            return Path.GetTempPath();
        }

        return fallbackDirectory;
    }

    private static IntPtr GetCurrentProcessHandle() => NativeMethods.GetCurrentProcess();

    private static uint GetCurrentProcessId() => NativeMethods.GetCurrentProcessId();

    [Flags]
    private enum MinidumpType : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithFullMemory = 0x00000002,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpFilterMemory = 0x00000008,
        MiniDumpScanMemory = 0x00000010,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
        MiniDumpFilterModulePaths = 0x00000080,
        MiniDumpWithProcessThreadData = 0x00000100,
        MiniDumpWithPrivateReadWriteMemory = 0x00000200,
        MiniDumpWithoutOptionalData = 0x00000400,
        MiniDumpWithFullMemoryInfo = 0x00000800,
        MiniDumpWithThreadInfo = 0x00001000,
        MiniDumpWithCodeSegs = 0x00002000,
        MiniDumpWithoutAuxiliaryState = 0x00004000,
        MiniDumpWithFullAuxiliaryState = 0x00008000,
        MiniDumpWithPrivateWriteCopyMemory = 0x00010000,
        MiniDumpIgnoreInaccessibleMemory = 0x00020000,
        MiniDumpWithTokenInformation = 0x00040000,
        MiniDumpWithModuleHeaders = 0x00080000,
        MiniDumpFilterTriage = 0x00100000,
        MiniDumpWithAvxXStateContext = 0x00200000,
        MiniDumpWithIptTrace = 0x00400000,
        MiniDumpScanInaccessiblePartialPages = 0x00800000,
        MiniDumpFilterWriteCombinedMemory = 0x01000000,
        MiniDumpValidTypeFlags = 0x01ffffff
    }

    private static class NativeMethods
    {
        [DllImport("dbghelp.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeHandle hFile,
            MinidumpType dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        [DllImport("kernel32.dll", SetLastError = false, CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = false, CallingConvention = CallingConvention.Winapi)]
        public static extern uint GetCurrentProcessId();
    }
}
