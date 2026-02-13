using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Nodus.Server.Services;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;
using System.Collections.ObjectModel;

namespace Nodus.Server.ViewModels;

public partial class ResultsViewModel : ObservableObject, IRecipient<VoteReceivedMessage>
{
    private readonly ExportService _exportService;
    private readonly VoteAggregatorService _aggregator;
    private readonly IFileSaverService _fileSaver;
    private readonly ILogger<ResultsViewModel> _logger;
    
    public ObservableCollection<Vote> Votes { get; } = new();

    public ResultsViewModel(
        VoteAggregatorService aggregator, 
        ExportService exportService,
        IFileSaverService fileSaver,
        ILogger<ResultsViewModel> logger)
    {
        _aggregator = aggregator;
        _exportService = exportService;
        _fileSaver = fileSaver;
        _logger = logger;
        
        // Initial Load (if any)
        var existing = _aggregator.GetAllVotes();
        foreach (var v in existing) Votes.Add(v);

        // Register for updates
        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(VoteReceivedMessage message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Votes.Insert(0, message.Value);
        });
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var eventId = Votes.FirstOrDefault()?.EventId ?? "default";
            var bytes = await _exportService.ExportToCsvAsync(eventId);
            var fileName = $"votes_{eventId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await SaveFileAsync(fileName, bytes, "text/csv");
        }
        catch (Exception ex)
        {
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", $"Export failed: {ex.Message}", "OK");
            }
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var eventId = Votes.FirstOrDefault()?.EventId ?? "default";
            var bytes = await _exportService.ExportToExcelAsync(eventId);
            var fileName = $"votes_{eventId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            await SaveFileAsync(fileName, bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        catch (Exception ex)
        {
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", $"Export failed: {ex.Message}", "OK");
            }
        }
    }

    private async Task SaveFileAsync(string fileName, byte[] data, string contentType)
    {
        var result = await _fileSaver.SaveAsync(fileName, data, contentType);
        
        if (result.IsSuccessful)
        {
            _logger.LogInformation("File exported successfully: {FilePath}", result.FilePath);
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Success", $"File saved to {result.FilePath}", "OK");
            }
        }
        else
        {
            _logger.LogError(result.Exception, "Failed to save file: {FileName}", fileName);
            if (result.Exception != null)
            {
                var page = Application.Current?.Windows[0].Page;
                if (page != null)
                {
                    await page.DisplayAlertAsync("Error", $"Save failed: {result.Exception.Message}", "OK");
                }
            }
        }
    }
}
