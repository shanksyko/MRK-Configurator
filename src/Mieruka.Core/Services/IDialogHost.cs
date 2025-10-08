using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Core.Services;

/// <summary>
/// Represents a component capable of showing dialogs to the operator.
/// </summary>
public interface IDialogHost
{
    /// <summary>
    /// Displays a confirmation dialog to the operator.
    /// </summary>
    /// <param name="title">Title displayed on the dialog.</param>
    /// <param name="message">Message shown to the operator.</param>
    /// <param name="cancellationToken">Token used to cancel the dialog.</param>
    /// <returns><c>true</c> when the operator confirms the action; otherwise <c>false</c>.</returns>
    Task<bool> ShowConfirmationAsync(string title, string message, CancellationToken cancellationToken);
}
