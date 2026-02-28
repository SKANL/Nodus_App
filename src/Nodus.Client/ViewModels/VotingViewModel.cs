using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using Nodus.Infrastructure.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Nodus.Client.ViewModels;

public partial class CategoryScore : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    // Score default 0 (not 5) after Minimum="0" fix in VotingPage.xaml slider.
    // This means the badge shows "—" until the judge explicitly interacts.
    [ObservableProperty] public partial double Score { get; set; } = 0;
    public double MaxScore { get; set; } = 10;
}

/// <summary>
/// Professional ViewModel for project voting.
/// Implements Result pattern, AsyncRelayCommand, and proper resource management.
/// </summary>
[QueryProperty(nameof(ProjectId), "ProjectId")]
[QueryProperty(nameof(EventId), "EventId")]
public partial class VotingViewModel : ObservableObject, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly IBleClientService _bleService;
    private readonly ILogger<VotingViewModel> _logger;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] public partial Project? CurrentProject { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Listo para Votar";
    [ObservableProperty] public partial bool IsSubmitting { get; set; }
    [ObservableProperty] public partial string Comment { get; set; } = "";

    public ObservableCollection<CategoryScore> Categories { get; } = new();

    // Progress tracking — Fix: Nielsen #1 (Visibility of System Status)
    // Tells the judge how many criteria they have rated out of the total.
    // Recomputed whenever any Category.Score changes or Categories collection changes.
    [ObservableProperty] public partial double EvaluationProgress { get; set; } = 0;
    [ObservableProperty] public partial string EvaluationProgressLabel { get; set; } = "0 de 0";

    [ObservableProperty]
    public partial string ProjectId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EventId { get; set; } = string.Empty;

    public VotingViewModel(
        IDatabaseService db,
        IBleClientService bleService,
        ILogger<VotingViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _logger = logger;
        _logger.LogInformation("VotingViewModel initialized");

        // Wire collection change events so EvaluationProgress updates
        // whenever a category is added/removed or its Score changes.
        Categories.CollectionChanged += (_, args) =>
        {
            if (args.NewItems != null)
                foreach (CategoryScore cat in args.NewItems)
                    cat.PropertyChanged += OnCategoryPropertyChanged;

            if (args.OldItems != null)
                foreach (CategoryScore cat in args.OldItems)
                    cat.PropertyChanged -= OnCategoryPropertyChanged;

            RecomputeProgress();
        };
    }

    private void OnCategoryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategoryScore.Score))
            RecomputeProgress();
    }

    /// <summary>Recomputes progress bar and label when scores change.</summary>
    private void RecomputeProgress()
    {
        int total = Categories.Count;
        // A score > 0 means the judge has intentionally rated this criterion.
        int rated  = Categories.Count(c => c.Score > 0);
        EvaluationProgress = total > 0 ? (double)rated / total : 0;
        EvaluationProgressLabel = $"{rated} de {total}";
    }

    /// <summary>
    /// Cancels the current vote and returns to the scanner.
    /// Fix: Nielsen #3 — User Control and Freedom.
    /// Judges who accidentally scan the wrong project need a clear exit.
    /// </summary>
    [RelayCommand]
    private async Task CancelVoteAsync()
    {
        bool confirmed = await Shell.Current.DisplayAlertAsync(
            "Cancelar Evaluación",
            "¿Deseas cancelar la evaluación de este proyecto y volver al escáner?",
            "Sí, cancelar",
            "Continuar evaluando");

        if (confirmed)
        {
            _logger.LogInformation("Vote for project {ProjectId} cancelled by judge", ProjectId);
            await Shell.Current.GoToAsync("..");
        }
    }

    /// <summary>Called by Shell when ProjectId query param arrives.</summary>
    partial void OnProjectIdChanged(string value) => TryInitialize();

    /// <summary>Called by Shell when EventId query param arrives.</summary>
    partial void OnEventIdChanged(string value) => TryInitialize();

    /// <summary>
    /// Triggers initialization only when ProjectId is available.
    /// EventId may arrive before or after ProjectId via Shell params.
    /// </summary>
    private void TryInitialize()
    {
        if (!string.IsNullOrEmpty(ProjectId))
            _ = InitializeAsync(ProjectId, EventId, _cts.Token);
    }

    public async Task InitializeAsync(string projectId, string eventId, CancellationToken ct = default)
    {
        EventId = eventId;
        await LoadProjectAsync(projectId, ct);
    }

    private async Task LoadProjectAsync(string projectId, CancellationToken ct)
    {
        StatusMessage = "Cargando Proyecto...";
        var result = await _db.GetProjectAsync(projectId, ct);

        if (result.IsSuccess)
        {
            CurrentProject = result.Value;

            // Load Rubric from Event
            var eventResult = await _db.GetEventAsync(EventId, ct);
            if (eventResult.IsSuccess && !string.IsNullOrWhiteSpace(eventResult.Value?.RubricJson))
            {
                ParseRubric(eventResult.Value.RubricJson);
            }
            else
            {
                // Fallback rubric — Score starts at 0 (not 5) so judges
                // explicitly rate each criterion. Matches Minimum="0" slider fix.
                Categories.Clear();
                Categories.Add(new CategoryScore { Name = "Diseño", Score = 0 });
                Categories.Add(new CategoryScore { Name = "Funcionalidad", Score = 0 });
            }

            StatusMessage = "Evaluar Proyecto";
            _logger.LogDebug("Project {ProjectId} loaded successfully", projectId);
        }
        else
        {
            StatusMessage = result.Error;
            _logger.LogWarning("Failed to load project {ProjectId}: {Error}", projectId, result.Error);
        }
    }

    private void ParseRubric(string rubricJson)
    {
        Categories.Clear();
        try
        {
            if (rubricJson.Contains("{"))
            {
                using var doc = JsonDocument.Parse(rubricJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    double max = prop.Value.TryGetDouble(out var v) ? v : 10;
                    // Score = 0 (not max/2) — judge starts with blank slate
                    Categories.Add(new CategoryScore { Name = prop.Name, Score = 0, MaxScore = max });
                }
            }
            else
            {
                var cats = rubricJson.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var cat in cats)
                {
                    // Score = 0: unrated until judge interacts with slider
                    Categories.Add(new CategoryScore { Name = cat.Trim(), Score = 0, MaxScore = 10 });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse rubric JSON: {Json}", rubricJson);
            Categories.Add(new CategoryScore { Name = "General", Score = 5 });
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SubmitVoteAsync(CancellationToken ct)
    {
        if (IsSubmitting || CurrentProject == null) return;

        IsSubmitting = true;
        StatusMessage = "Guardando...";

        try
        {
            var result = await PerformVoteSubmissionAsync(ct);

            if (result.IsSuccess)
            {
                StatusMessage = "¡Voto Enviado!";
                _logger.LogInformation("Vote for project {ProjectId} submitted successfully", CurrentProject.Id);

                await Task.Delay(1000, ct);
                // Use Shell navigation (..) instead of Navigation.PopAsync —
                // Shell manages its own stack; mixing both causes inconsistent state.
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                StatusMessage = $"Error: {result.Error}";
                _logger.LogWarning("Vote submission failed: {Error}", result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Envío cancelado";
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private async Task<Result> PerformVoteSubmissionAsync(CancellationToken ct)
    {
        // 1. Haptic Feedback
        if (Vibration.Default.IsSupported)
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(50));

        // 2. Prepare Vote
        var judgeId = await SecureStorage.Default.GetAsync(Nodus.Shared.NodusConstants.KEY_JUDGE_NAME) ?? "Unknown";

        var payload = new Dictionary<string, object>();
        foreach (var cat in Categories)
        {
            payload[cat.Name] = cat.Score;
        }
        if (!string.IsNullOrWhiteSpace(Comment))
            payload["comment"] = Comment.Trim();

        var vote = new Vote
        {
            EventId = EventId,
            ProjectId = CurrentProject!.Id,
            JudgeId = judgeId,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = SyncStatus.Pending
        };

        // 3. Save Local (Atomic Transaction)
        var saveResult = await _db.SaveVoteAsync(vote, ct);
        if (saveResult.IsFailure) return saveResult;

        // 4. Attempt BLE Sync
        StatusMessage = "Sincronizando vía Red Firefly...";
        var syncResult = await _bleService.SendVoteAsync(vote, ct);

        if (syncResult.IsSuccess)
        {
            vote.Status = SyncStatus.Synced;
            vote.SyncedAtUtc = DateTime.UtcNow;
            _logger.LogInformation("Vote {VoteId} synced over BLE", vote.Id);

            // Update status in local DB
            await _db.SaveVoteAsync(vote, ct);
            return Result.Success();
        }
        else
        {
            _logger.LogInformation("BLE Sync failed for Vote {VoteId}: {Error}. Kept as pending.", vote.Id, syncResult.Error);
            return Result.Success(); // Success at application level (saved locally)
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _logger.LogInformation("VotingViewModel disposed");
    }
}
