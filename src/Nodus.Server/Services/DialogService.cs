using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;

namespace Nodus.Server.Services;

/// <summary>
/// MAUI implementation of IDialogService for Nodus.Server.
/// Thread-safe and null-safe implementation for .NET 10.
/// </summary>
public class DialogService : IDialogService
{
    private readonly ILogger<DialogService> _logger;

    public DialogService(ILogger<DialogService> logger)
    {
        _logger = logger;
    }

    public async Task ShowErrorAsync(string message, string title = "Error")
    {
        await ShowAlertAsync(title, message, "Aceptar");
    }

    public async Task ShowSuccessAsync(string message, string title = "Éxito")
    {
        await ShowAlertAsync(title, message, "Aceptar");
    }

    public async Task ShowInfoAsync(string message, string title = "Información")
    {
        await ShowAlertAsync(title, message, "Aceptar");
    }

    public async Task<bool> ShowConfirmAsync(string message, string title = "Confirmar", string accept = "Sí", string cancel = "No")
    {
        try
        {
            if (Application.Current is null || Application.Current.Windows.Count == 0)
            {
                _logger.LogWarning("Cannot show confirm dialog: No windows available");
                return false;
            }

            var page = Application.Current.Windows[0].Page;
            if (page == null)
            {
                _logger.LogWarning("Cannot show confirm dialog: Page is null");
                return false;
            }

            return await page.DisplayAlertAsync(title, message, accept, cancel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show confirm dialog");
            return false;
        }
    }

    public async Task<string?> ShowPromptAsync(string title, string message, string placeholder = "", int maxLength = 50)
    {
        try
        {
            if (Application.Current is null || Application.Current.Windows.Count == 0)
            {
                _logger.LogWarning("Cannot show prompt: No windows available");
                return null;
            }

            var page = Application.Current.Windows[0].Page;
            if (page == null)
            {
                _logger.LogWarning("Cannot show prompt: Page is null");
                return null;
            }

            return await page.DisplayPromptAsync(
                title,
                message,
                placeholder: placeholder,
                maxLength: maxLength,
                keyboard: Keyboard.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show prompt");
            return null;
        }
    }

    private async Task ShowAlertAsync(string title, string message, string button)
    {
        try
        {
            if (Application.Current is null || Application.Current.Windows.Count == 0)
            {
                _logger.LogWarning("Cannot show alert: No windows available");
                return;
            }

            var page = Application.Current.Windows[0].Page;
            if (page == null)
            {
                _logger.LogWarning("Cannot show alert: Page is null");
                return;
            }

            await page.DisplayAlertAsync(title, message, button);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show alert: {Title} - {Message}", title, message);
        }
    }
}
