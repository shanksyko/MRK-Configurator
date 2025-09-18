using Serilog;

namespace Mieruka.App;

internal static class Program
{
    private static int Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .CreateLogger();

        try
        {
            Log.Information("Mieruka application bootstrap completed.");
            return 0;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
