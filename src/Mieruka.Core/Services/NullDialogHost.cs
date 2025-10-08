using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Core.Services;

/// <summary>
/// Dialog host that automatically approves all requests.
/// </summary>
public sealed class NullDialogHost : IDialogHost
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="NullDialogHost"/>.
    /// </summary>
    public static NullDialogHost Instance { get; } = new();

    private NullDialogHost()
    {
    }

    /// <inheritdoc />
    public Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        return Task.FromResult(true);
    }
}
