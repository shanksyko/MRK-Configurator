using System;
using System.Threading;
using Serilog;

namespace Mieruka.Core.Diagnostics;

/// <summary>
/// Guard genérico para evitar StackOverflow por recursão/reentrância excessiva.
/// Uses [ThreadStatic] instead of AsyncLocal for minimal overhead in hot paths.
/// </summary>
public readonly struct StackGuard : IDisposable
{
    private const int MaxDepth = 32;

    [ThreadStatic]
    private static int _depth;

    private readonly bool _entered;

    /// <summary>
    /// Gets a value indicating whether the guard entered the protected section.
    /// </summary>
    public bool Entered => _entered;

    /// <summary>
    /// Initializes a new instance of the <see cref="StackGuard"/> struct.
    /// </summary>
    /// <param name="name">Name used for logging context.</param>
    public StackGuard(string name)
    {
        var nextDepth = _depth + 1;
        if (nextDepth > MaxDepth)
        {
            Log.Warning(
                "StackGuard triggered for {Name}. depth={Depth} max={MaxDepth}",
                name,
                nextDepth,
                MaxDepth);
            _entered = false;
        }
        else
        {
            _depth = nextDepth;
            _entered = true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_entered)
        {
            var currentDepth = _depth;
            if (currentDepth <= 0)
            {
                _depth = 0;
            }
            else
            {
                _depth = currentDepth - 1;
            }
        }
    }
}
