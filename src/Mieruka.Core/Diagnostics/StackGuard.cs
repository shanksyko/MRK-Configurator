using System;
using Serilog;

namespace Mieruka.Core.Diagnostics;

/// <summary>
/// Guard genérico para evitar StackOverflow por recursão/reentrância excessiva.
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
        var depth = ++_depth;
        if (depth > MaxDepth)
        {
            Log.Warning(
                "StackGuard triggered for {Name}. depth={Depth} max={MaxDepth}",
                name,
                depth,
                MaxDepth);
            _entered = false;
            --_depth;
        }
        else
        {
            _entered = true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_entered)
        {
            --_depth;
        }
    }
}
