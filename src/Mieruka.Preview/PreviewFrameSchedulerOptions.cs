using System;

namespace Mieruka.Preview;

public sealed class PreviewFrameSchedulerOptions
{
    public const double DefaultFramesPerSecond = 30d;

    public static PreviewFrameSchedulerOptions Default { get; } = new();

    private double _framesPerSecond = DefaultFramesPerSecond;

    public double FramesPerSecond
    {
        get => _framesPerSecond;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(FramesPerSecond), "Frames per second must be positive.");
            }

            _framesPerSecond = value;
        }
    }
}
