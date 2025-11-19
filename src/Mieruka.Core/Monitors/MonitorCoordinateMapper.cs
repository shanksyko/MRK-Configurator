using System;
using System.Drawing;
using Mieruka.Core.Models;

namespace Mieruka.Core.Monitors;

/// <summary>
/// Provides utilities to convert coordinates between monitor, preview and UI coordinate spaces.
/// </summary>
public sealed class MonitorCoordinateMapper
{
    private readonly int _monitorWidth;
    private readonly int _monitorHeight;
    public float ScaleX { get; }
    public float ScaleY { get; }

    public MonitorCoordinateMapper(MonitorInfo monitor)
    {
        Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _monitorWidth = Math.Max(1, monitor.Width > 0 ? monitor.Width : monitor.Bounds.Width);
        _monitorHeight = Math.Max(1, monitor.Height > 0 ? monitor.Height : monitor.Bounds.Height);
        PreviewResolution = monitor.GetPreviewResolution();

        if (!PreviewResolution.HasValidSize)
        {
            PreviewResolution = PreviewResolution.FromDimensions(_monitorWidth, _monitorHeight);
        }

        ScaleX = (float)CalculateScale(PreviewResolution.LogicalWidth, _monitorWidth);
        ScaleY = (float)CalculateScale(PreviewResolution.LogicalHeight, _monitorHeight);
    }

    public MonitorInfo Monitor { get; }

    public PreviewResolution PreviewResolution { get; private set; }

    public Point MonitorToPreview(Point monitorPoint)
    {
        var x = ScaleCoordinate(monitorPoint.X, ScaleX, PreviewResolution.LogicalWidth);
        var y = ScaleCoordinate(monitorPoint.Y, ScaleY, PreviewResolution.LogicalHeight);
        return new Point(x, y);
    }

    public Rectangle MonitorToPreview(Rectangle monitorRect)
    {
        var x = ScaleCoordinate(monitorRect.X, ScaleX, PreviewResolution.LogicalWidth);
        var y = ScaleCoordinate(monitorRect.Y, ScaleY, PreviewResolution.LogicalHeight);
        var width = ScaleLength(monitorRect.Width, ScaleX, PreviewResolution.LogicalWidth);
        var height = ScaleLength(monitorRect.Height, ScaleY, PreviewResolution.LogicalHeight);
        return new Rectangle(x, y, width, height);
    }

    public Point PreviewToMonitor(Point previewPoint)
    {
        var x = ScaleCoordinate(previewPoint.X, MonitorWidthPerPreviewPixelX, _monitorWidth);
        var y = ScaleCoordinate(previewPoint.Y, MonitorHeightPerPreviewPixelY, _monitorHeight);
        return new Point(x, y);
    }

    public Rectangle PreviewToMonitor(Rectangle previewRect)
    {
        var x = ScaleCoordinate(previewRect.X, MonitorWidthPerPreviewPixelX, _monitorWidth);
        var y = ScaleCoordinate(previewRect.Y, MonitorHeightPerPreviewPixelY, _monitorHeight);
        var width = ScaleLength(previewRect.Width, MonitorWidthPerPreviewPixelX, _monitorWidth);
        var height = ScaleLength(previewRect.Height, MonitorHeightPerPreviewPixelY, _monitorHeight);
        return new Rectangle(x, y, width, height);
    }

    public Point UiToPreview(Point uiPoint, RectangleF displayRect)
    {
        if (!PreviewResolution.HasValidSize || displayRect.Width <= 0f || displayRect.Height <= 0f)
        {
            return Point.Empty;
        }

        var relativeX = (uiPoint.X - displayRect.X) / displayRect.Width;
        var relativeY = (uiPoint.Y - displayRect.Y) / displayRect.Height;

        relativeX = (float)Clamp01(relativeX);
        relativeY = (float)Clamp01(relativeY);

        var x = (int)Math.Round(relativeX * PreviewResolution.LogicalWidth, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(relativeY * PreviewResolution.LogicalHeight, MidpointRounding.AwayFromZero);

        x = ClampDimension(x, 0, PreviewResolution.LogicalWidth);
        y = ClampDimension(y, 0, PreviewResolution.LogicalHeight);
        return new Point(x, y);
    }

    public RectangleF PreviewToUi(Rectangle previewRect, RectangleF displayRect)
    {
        if (!PreviewResolution.HasValidSize || displayRect.Width <= 0f || displayRect.Height <= 0f)
        {
            return RectangleF.Empty;
        }

        var scaleX = displayRect.Width / PreviewResolution.LogicalWidth;
        var scaleY = displayRect.Height / PreviewResolution.LogicalHeight;

        var x = (float)(displayRect.X + (previewRect.X * scaleX));
        var y = (float)(displayRect.Y + (previewRect.Y * scaleY));
        var width = (float)(previewRect.Width * scaleX);
        var height = (float)(previewRect.Height * scaleY);
        return new RectangleF(x, y, width, height);
    }

    public Point UiToMonitor(Point uiPoint, RectangleF displayRect)
    {
        var previewPoint = UiToPreview(uiPoint, displayRect);
        return PreviewToMonitor(previewPoint);
    }

    private static int ScaleCoordinate(int coordinate, double scale, int max)
    {
        var scaled = (int)Math.Round(coordinate * scale, MidpointRounding.AwayFromZero);
        return ClampDimension(scaled, 0, max);
    }

    private static int ScaleLength(int length, double scale, int max)
    {
        if (length <= 0)
        {
            return 0;
        }

        var scaled = (int)Math.Round(length * scale, MidpointRounding.AwayFromZero);
        return ClampDimension(scaled, 0, max);
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Min(1d, Math.Max(0d, value));
    }

    private static int ClampDimension(int value, int min, int max)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (max == min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private static double CalculateScale(int logicalDimension, int monitorDimension)
    {
        if (logicalDimension <= 0 || monitorDimension <= 0)
        {
            return 0d;
        }

        return logicalDimension / (double)monitorDimension;
    }

    private double MonitorWidthPerPreviewPixelX
        => _monitorWidth / (double)Math.Max(1, PreviewResolution.LogicalWidth);

    private double MonitorHeightPerPreviewPixelY
        => _monitorHeight / (double)Math.Max(1, PreviewResolution.LogicalHeight);
}
