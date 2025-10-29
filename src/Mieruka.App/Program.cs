using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Mieruka.App.Forms;
using Mieruka.App.Services.Ui;
using Serilog;
using Serilog.Enrichers;
using Serilog.Events;

namespace Mieruka.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ConfigureLogging();

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.AddMessageFilter(new MouseMoveCoalescer(16));
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            Log.Information("Iniciando Mieruka Configurator.");

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

    private static void ConfigureLogging()
    {
        const string directoryName = "MierukaConfiguratorPro";
        const string logsFolderName = "logs";
        const string logFileName = "mieruka-.log";
        const string outputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] (T{ThreadId}) {SourceContext}: {Message:lj}{NewLine}{Exception}";

        var minimumLevel = LogEventLevel.Information;
#if DEBUG
        minimumLevel = LogEventLevel.Debug;
#endif

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Path.GetTempPath();
        }

        var logDirectory = Path.Combine(baseDirectory, directoryName, logsFolderName);

        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch
        {
            // Ignorar falhas ao criar diretórios para não impactar a inicialização.
        }

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "MierukaConfigurator")
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: Path.Combine(logDirectory, logFileName),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: outputTemplate);

#if DEBUG
        configuration = configuration.WriteTo.Console(outputTemplate: outputTemplate);
#endif

        Log.Logger = configuration.CreateLogger();
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
