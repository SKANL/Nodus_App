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
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private double _score = 5;
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

    [ObservableProperty] private Project? _currentProject;
    [ObservableProperty] private string _statusMessage = "Ready to Vote";
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private string _comment = "";
    
    public ObservableCollection<CategoryScore> Categories { get; } = new();

    [ObservableProperty]
    private string _projectId = string.Empty;

    [ObservableProperty]
    private string _eventId = string.Empty;

    public VotingViewModel(
        IDatabaseService db, 
        IBleClientService bleService,
        ILogger<VotingViewModel> logger)
    {
        _db = db;
        _bleService = bleService;
        _logger = logger;
        _logger.LogInformation("VotingViewModel initialized");
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
        _eventId = eventId;
        await LoadProjectAsync(projectId, ct);
    }

    private async Task LoadProjectAsync(string projectId, CancellationToken ct)
    {
        StatusMessage = "Loading Project...";
        var result = await _db.GetProjectAsync(projectId, ct);
        
        if (result.IsSuccess)
        {
            CurrentProject = result.Value;
            
            // Load Rubric from Event
            var eventResult = await _db.GetEventAsync(_eventId, ct);
            if (eventResult.IsSuccess && !string.IsNullOrWhiteSpace(eventResult.Value.RubricJson))
            {
                ParseRubric(eventResult.Value.RubricJson);
            }
            else
            {
                // Fallback rubric
                Categories.Clear();
                Categories.Add(new CategoryScore { Name = "Design", Score = 5 });
                Categories.Add(new CategoryScore { Name = "Functionality", Score = 5 });
            }

            StatusMessage = "Evaluate Project";
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
                    Categories.Add(new CategoryScore { Name = prop.Name, Score = max / 2, MaxScore = max });
                }
            }
            else
            {
                var cats = rubricJson.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var cat in cats)
                {
                    Categories.Add(new CategoryScore { Name = cat.Trim(), Score = 5, MaxScore = 10 });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse rubric JSON: {Json}", rubricJson);
            Categories.Add(new CategoryScore { Name = "Overall", Score = 5 });
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SubmitVoteAsync(CancellationToken ct)
    {
        if (IsSubmitting || CurrentProject == null) return;
        
        IsSubmitting = true;
        StatusMessage = "Saving...";

        try
        {
            var result = await PerformVoteSubmissionAsync(ct);

            if (result.IsSuccess)
            {
                StatusMessage = "Vote Submitted!";
                _logger.LogInformation("Vote for project {ProjectId} submitted successfully", CurrentProject.Id);
                
                await Task.Delay(1000, ct);
                await MainThread.InvokeOnMainThreadAsync(async () => {
                     await Application.Current!.Windows[0].Page!.Navigation.PopAsync();
                });
            }
            else
            {
                StatusMessage = $"Error: {result.Error}";
                _logger.LogWarning("Vote submission failed: {Error}", result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Submission cancelled";
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
            EventId = _eventId,
            ProjectId = CurrentProject!.Id,
            JudgeId = judgeId,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = SyncStatus.Pending
        };

        // 3. Save Local (Atomic Transaction)
        var saveResult = await _db.SaveVoteAsync(vote, ct);
        if (saveResult.IsFailure) return saveResult;

        // 4. Attempt BLE Sync
        StatusMessage = "Syncing via Firefly Swarm...";
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
