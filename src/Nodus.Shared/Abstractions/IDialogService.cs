using System.Threading.Tasks;

namespace Nodus.Shared.Abstractions;

/// <summary>
/// Abstraction for showing dialogs to the user.
/// Centralizes dialog logic and makes ViewModels more testable.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an error alert to the user.
    /// </summary>
    Task ShowErrorAsync(string message, string title = "Error");

    /// <summary>
    /// Shows a success alert to the user.
    /// </summary>
    Task ShowSuccessAsync(string message, string title = "Success");

    /// <summary>
    /// Shows an informational alert to the user.
    /// </summary>
    Task ShowInfoAsync(string message, string title = "Information");

    /// <summary>
    /// Shows a confirmation dialog with Yes/No options.
    /// </summary>
    /// <returns>True if user confirmed, false otherwise</returns>
    Task<bool> ShowConfirmAsync(string message, string title = "Confirm", string accept = "Yes", string cancel = "No");

    /// <summary>
    /// Shows a prompt dialog for text input.
    /// </summary>
    /// <returns>The entered text, or null if cancelled</returns>
    Task<string?> ShowPromptAsync(string title, string message, string placeholder = "", int maxLength = 50);
}
