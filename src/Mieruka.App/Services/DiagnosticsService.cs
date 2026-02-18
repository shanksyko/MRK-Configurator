using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.App.Services;

/// <summary>
/// Exposes a lightweight HTTP endpoint that reports runtime diagnostics for the application.
/// </summary>
public sealed class DiagnosticsService : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly HttpListener _listener;
    private readonly Func<DiagnosticsReport> _reportProvider;
    private readonly string _healthPath;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _running;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsService"/> class using the default endpoint.
    /// </summary>
    /// <param name="reportProvider">Factory that supplies the diagnostics payload.</param>
    public DiagnosticsService(Func<DiagnosticsReport> reportProvider)
        : this(reportProvider, "http://localhost:5005/", "/health")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiagnosticsService"/> class.
    /// </summary>
    /// <param name="reportProvider">Factory that supplies the diagnostics payload.</param>
    /// <param name="prefix">HTTP listener prefix (for example, <c>http://localhost:5005/</c>).</param>
    /// <param name="healthPath">Relative path that should expose the diagnostics payload.</param>
    public DiagnosticsService(Func<DiagnosticsReport> reportProvider, string prefix, string healthPath)
    {
        _reportProvider = reportProvider ?? throw new ArgumentNullException(nameof(reportProvider));

        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("The listener prefix must be defined.", nameof(prefix));
        }

        if (string.IsNullOrWhiteSpace(healthPath))
        {
            throw new ArgumentException("The health path must be defined.", nameof(healthPath));
        }

        _healthPath = NormalizePath(healthPath);
        _listener = new HttpListener();
        _listener.Prefixes.Add(NormalizePrefix(prefix));
    }

    /// <summary>
    /// Starts the HTTP listener if it is not already running.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (_running)
            {
                return;
            }

            _cts = new CancellationTokenSource();

            try
            {
                _listener.Start();
            }
            catch
            {
                _cts.Dispose();
                _cts = null;
                throw;
            }

            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token), CancellationToken.None);
            _running = true;
        }
    }

    /// <summary>
    /// Stops the HTTP listener when it is active.
    /// </summary>
    public async Task StopAsync()
    {
        Task? listenerTask;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            listenerTask = _listenerTask;
            cts = _cts;
            _listenerTask = null;
            _cts = null;
        }

        try
        {
            cts?.Cancel();
        }
        finally
        {
            try
            {
                if (_listener.IsListening)
                {
                    _listener.Stop();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (listenerTask is not null)
        {
            try
            {
                await listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cts?.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (_listener.IsListening)
                {
                    continue;
                }

                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (context is null)
            {
                continue;
            }

            try
            {
                await ProcessRequestAsync(context).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.Close();
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";
        if (!IsHealthPath(path))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        DiagnosticsReport? report;
        try
        {
            report = _reportProvider();
        }
        catch
        {
            report = null;
        }

        if (report is null)
        {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            response.Close();
            return;
        }

        var payload = JsonSerializer.Serialize(report with { Timestamp = DateTimeOffset.UtcNow }, SerializerOptions);
        var buffer = Encoding.UTF8.GetBytes(payload);

        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = buffer.Length;

        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        response.Close();
    }

    private bool IsHealthPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path;
        if (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized[..^1];
        }

        return string.Equals(normalized, _healthPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePrefix(string prefix)
    {
        return prefix.EndsWith("/", StringComparison.Ordinal)
            ? prefix
            : prefix + "/";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal))
        {
            path = path[..^1];
        }

        return path;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DiagnosticsService));
        }
    }

    /// <summary>
    /// Releases resources used by the service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        _listener.Close();
    }

    /// <summary>
    /// Represents the payload returned by the diagnostics endpoint.
    /// </summary>
    public sealed record class DiagnosticsReport
    {
        /// <summary>
        /// Gets or sets the high level status of the application.
        /// </summary>
        public string Status { get; init; } = "Healthy";

        /// <summary>
        /// Gets or sets the number of monitors currently active.
        /// </summary>
        public int MonitorsActive { get; init; }

        /// <summary>
        /// Gets or sets the preview frames per second metric.
        /// </summary>
        public double PreviewFps { get; init; }

        /// <summary>
        /// Gets or sets the number of cycle items currently eligible for playback.
        /// </summary>
        public int CyclesRunning { get; init; }

        /// <summary>
        /// Gets or sets the number of watchdogs that are active.
        /// </summary>
        public int WatchdogsActive { get; init; }

        /// <summary>
        /// Gets or sets the timestamp when the report was generated.
        /// </summary>
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    }
}
