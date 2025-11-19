using System;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;

namespace Mieruka.Preview.Contracts;

public enum PreviewIpcMessageKind
{
    StartCommand = 1,
    StopCommand = 2,
    Frame = 3,
    Status = 4,
}

public enum PreviewBackendKind
{
    Gpu,
    Gdi,
}

public sealed record PreviewBackendInfo(
    PreviewBackendKind Backend,
    double TargetFps,
    string? Description = null);

public sealed record PreviewStartCommand(string MonitorId, bool PreferGpu);

public sealed record PreviewStopCommand;

public sealed record PreviewStatusMessage(
    PreviewBackendInfo Backend,
    string Status,
    string? Detail = null);

[SupportedOSPlatform("windows")]
public sealed record PreviewFrameMessage(
    PreviewBackendInfo Backend,
    int Width,
    int Height,
    int Stride,
    PixelFormat PixelFormat,
    long FrameIndex,
    DateTimeOffset Timestamp,
    byte[] Buffer)
{
    [JsonIgnore]
    public int ExpectedLength => Math.Max(0, Height * Stride);
}
