using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;

namespace Nodus.Client.Services;

/// <summary>
/// MAUI implementation of IDialogService using Windows[0].Page.
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
        await ShowAlertAsync(title, message, "OK");
    }

    public async Task ShowSuccessAsync(string message, string title = "Success")
    {
        await ShowAlertAsync(title, message, "OK");
    }

    public async Task ShowInfoAsync(string message, string title = "Information")
    {
        await ShowAlertAsync(title, message, "OK");
    }

    public async Task<bool> ShowConfirmAsync(string message, string title = "Confirm", string accept = "Yes", string cancel = "No")
    {
        try
        {
            var page = GetCurrentPage();
            if (page is null)
            {
                _logger.LogWarning("Cannot show confirm dialog: no active page");
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
            var page = GetCurrentPage();
            if (page is null)
            {
                _logger.LogWarning("Cannot show prompt: no active page");
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
            var page = GetCurrentPage();
            if (page is null)
            {
                _logger.LogWarning("Cannot show alert: no active page");
                return;
            }
            await page.DisplayAlertAsync(title, message, button);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show alert: {Title} - {Message}", title, message);
        }
    }

    /// <summary>
    /// Returns the current top-level Page using the safe FirstOrDefault pattern.
    /// Windows[0] index access is avoided: it can throw IndexOutOfRangeException
    /// when the window list is momentarily empty during navigation transitions.
    /// </summary>
    private static Page? GetCurrentPage()
        => Application.Current?.Windows.FirstOrDefault()?.Page;
}
