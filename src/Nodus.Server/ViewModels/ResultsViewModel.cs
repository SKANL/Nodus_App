using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Nodus.Server.Services;
using Nodus.Shared.Models;
using System.Collections.ObjectModel;

namespace Nodus.Server.ViewModels;

public partial class ResultsViewModel : ObservableObject, IRecipient<VoteReceivedMessage>
{
    private readonly ExportService _exportService;
    
    public ObservableCollection<Vote> Votes { get; } = new();

    public ResultsViewModel(VoteAggregatorService aggregator, ExportService exportService)
    {
        _aggregator = aggregator;
        _exportService = exportService;
        
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
            await SaveFileAsync(fileName, bytes);
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error", $"Export failed: {ex.Message}", "OK");
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
            await SaveFileAsync(fileName, bytes);
        }
        catch (Exception ex)
        {
            await Application.Current!.MainPage!.DisplayAlert("Error", $"Export failed: {ex.Message}", "OK");
        }
    }

    private async Task SaveFileAsync(string fileName, byte[] data)
    {
        using var stream = new MemoryStream(data);
        var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream);
        
        if (result.IsSuccessful)
        {
            await Application.Current!.MainPage!.DisplayAlert("Success", $"File saved to {result.FilePath}", "OK");
        }
        else
        {
            if (result.Exception != null)
            {
                await Application.Current!.MainPage!.DisplayAlert("Error", $"Save failed: {result.Exception.Message}", "OK");
            }
        }
    }
}
