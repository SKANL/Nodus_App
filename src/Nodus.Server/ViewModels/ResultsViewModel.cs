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
    private readonly IDatabaseService _db;

    // ── Observable State ───────────────────────────────────────────────────
    [ObservableProperty] public partial ObservableCollection<ProjectLeaderboardEntry> Top3 { get; set; } = new();
    [ObservableProperty] public partial ObservableCollection<ProjectLeaderboardEntry> Remaining { get; set; } = new();
    [ObservableProperty] public partial bool HasResults { get; set; }

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
            await ShowAlertAsync("Error", $"Exportación fallida: {ex.Message}");
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
            await ShowAlertAsync("Error", $"Exportación fallida: {ex.Message}");
        }
    }

    private async Task SaveFileAsync(string fileName, byte[] data, string contentType)
    {
        var result = await _fileSaver.SaveAsync(fileName, data, contentType);

        if (result.IsSuccessful)
        {
            _logger.LogInformation("File exported successfully: {FilePath}", result.FilePath);
            await ShowAlertAsync("Éxito", $"Archivo guardado en {result.FilePath}");
        }
        else
        {
            _logger.LogError(result.Exception, "Failed to save file: {FileName}", fileName);
            if (result.Exception != null)
                await ShowAlertAsync("Error", $"Error al guardar: {result.Exception.Message}");
        }
    }

    /// <summary>Safe page access — avoids Windows[0] index-out-of-range.</summary>
    private static Task ShowAlertAsync(string title, string message)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        return page is not null
            ? page.DisplayAlertAsync(title, message, "OK")
            : Task.CompletedTask;
    }
}
