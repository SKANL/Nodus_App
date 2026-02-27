using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Nodus.Server.Services;
using Nodus.Shared.Services; // Ensure VoteAggregatorService is found
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

    // ── Observable State ───────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ProjectLeaderboardEntry> _top3 = new();
    [ObservableProperty] private ObservableCollection<ProjectLeaderboardEntry> _remaining = new();
    [ObservableProperty] private bool _hasResults;

    public ResultsViewModel(
        IDatabaseService db,
        VoteAggregatorService aggregator,
        ExportService exportService,
        IFileSaverService fileSaver,
        ILogger<ResultsViewModel> logger)
    {
        _db = db;
        _aggregator = aggregator;
        _exportService = exportService;
        _fileSaver = fileSaver;
        _logger = logger;

        RefreshLeaderboard();

        // Register for updates
        WeakReferenceMessenger.Default.Register(this);
    }

    private readonly IDatabaseService _db;

    private async void RefreshLeaderboard()
    {
        var data = _aggregator.GetLeaderboard();
        var projects = await _db.GetAllProjectsAsync();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var enriched = data.Select(entry =>
            {
                var project = projects.ValueOr(null)?.FirstOrDefault(p => p.Id == entry.ProjectId);
                entry.ProjectName = project?.Name ?? entry.ProjectId;
                return entry;
            }).ToList();

            Top3.Clear();
            foreach (var item in enriched.Take(3)) Top3.Add(item);

            Remaining.Clear();
            foreach (var item in enriched.Skip(3)) Remaining.Add(item);

            HasResults = enriched.Any();
        });
    }

    public void Receive(VoteReceivedMessage message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshLeaderboard();
        });
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var eventId = _aggregator.GetAllVotes().FirstOrDefault()?.EventId ?? "default";
            var bytes = await _exportService.ExportToCsvAsync(eventId);
            var fileName = $"votes_{eventId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await SaveFileAsync(fileName, bytes, "text/csv");
        }
        catch (Exception ex)
        {
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", $"Exportación fallida: {ex.Message}", "OK");
            }
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var eventId = _aggregator.GetAllVotes().FirstOrDefault()?.EventId ?? "default";
            var bytes = await _exportService.ExportToExcelAsync(eventId);
            var fileName = $"votes_{eventId}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            await SaveFileAsync(fileName, bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }
        catch (Exception ex)
        {
            var page = Application.Current?.Windows[0].Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", $"Exportación fallida: {ex.Message}", "OK");
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
                await page.DisplayAlertAsync("Éxito", $"Archivo guardado en {result.FilePath}", "OK");
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
                    await page.DisplayAlertAsync("Error", $"Error al guardar: {result.Exception.Message}", "OK");
                }
            }
        }
    }
}
