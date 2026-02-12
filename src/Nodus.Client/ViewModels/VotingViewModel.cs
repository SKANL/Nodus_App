using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

using Nodus.Shared.Abstractions;
using Nodus.Shared.Common;
using Nodus.Shared.Models;
using System.Text.Json;

namespace Nodus.Client.ViewModels;

/// <summary>
/// Professional ViewModel for project voting.
/// Implements Result pattern, AsyncRelayCommand, and proper resource management.
/// </summary>
[QueryProperty(nameof(ProjectId), "ProjectId")]
public partial class VotingViewModel : ObservableObject, IDisposable
{
    private readonly IDatabaseService _db;
    private readonly IBleClientService _bleService;
    private readonly ILogger<VotingViewModel> _logger;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private Project? _currentProject;
    [ObservableProperty] private double _designScore = 5;
    [ObservableProperty] private double _functionalityScore = 5;
    [ObservableProperty] private string _statusMessage = "Ready to Vote";
    [ObservableProperty] private bool _isSubmitting;

    private string _eventId = string.Empty;

    [ObservableProperty]
    private string _projectId = string.Empty;

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

    partial void OnProjectIdChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            // Trigger initialization when ProjectId is set via Shell
            _ = InitializeAsync(value, _eventId, _cts.Token);
        }
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
            StatusMessage = "Evaluate Project";
            _logger.LogDebug("Project {ProjectId} loaded successfully", projectId);
        }
        else
        {
            StatusMessage = result.Error;
            _logger.LogWarning("Failed to load project {ProjectId}: {Error}", projectId, result.Error);
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
        var judgeId = await SecureStorage.Default.GetAsync("judge_id") ?? "Unknown";
        var payload = new Dictionary<string, double>
        {
            { "Design", DesignScore },
            { "Functionality", FunctionalityScore }
        };

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
