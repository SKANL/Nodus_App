using CommunityToolkit.Mvvm.Messaging;
using Nodus.Shared.Models;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Common; // For Result
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nodus.Shared.Services;

public class VoteAggregatorService
{
    private readonly ConcurrentDictionary<string, List<Vote>> _votesByProject = new();
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<VoteAggregatorService> _logger;

    public VoteAggregatorService(IDatabaseService databaseService, ILogger<VoteAggregatorService> logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    // Message class definition moved inside or kept here? 
    // Usually messages are separate or nested. Previous file had it outside.
    // I'll keep it here for simplicity or define it in Shared.Models if used elsewhere.
    // For now, I'll keep it in this file but public.
    
    public async Task<Result> ProcessVoteAsync(Vote vote)
    {
        // 1. Validate Timestamp
        var voteTime = DateTimeOffset.FromUnixTimeSeconds(vote.Timestamp).UtcDateTime;
        if (voteTime > DateTime.UtcNow.AddMinutes(5)) // Allow small clock skew
        {
            _logger.LogWarning("Rejected vote {Id} from future: {Time}", vote.Id, voteTime);
            return Result.Failure($"Timestamp in future: {voteTime}");
        }

        if (voteTime < DateTime.UtcNow.AddDays(-365)) // Too old
        {
            _logger.LogWarning("Rejected vote {Id} too old: {Time}", vote.Id, voteTime);
            return Result.Failure($"Timestamp too old: {voteTime}");
        }

        // 2. Validate Payload JSON
        try
        {
            if (string.IsNullOrWhiteSpace(vote.PayloadJson))
                return Result.Failure("Empty Payload");

            // Simple validation: must be valid JSON object
            using (JsonDocument doc = JsonDocument.Parse(vote.PayloadJson))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Result.Failure("Invalid Payload format (not an object)");
                
                // Optional: Check for 'score' or required fields?
                // Aggressive test expects "Invalid Payload" if malformed.
                // Test passes "INVALID JSON" string which Parse throws on.
            }
        }
        catch (JsonException)
        {
            _logger.LogWarning("Rejected vote {Id} with invalid JSON", vote.Id);
            return Result.Failure("Invalid Payload JSON");
        }

        // 3. Persist to DB (for resilience)
        var saveResult = await _databaseService.SaveVoteAsync(vote);
        if (saveResult.IsFailure)
        {
             _logger.LogError("Failed to save vote {Id}: {Error}", vote.Id, saveResult.Error);
             return Result.Failure($"Database error: {saveResult.Error}");
        }

        // 4. Update In-Memory Aggregation
        _votesByProject.AddOrUpdate(vote.ProjectId, 
            new List<Vote> { vote }, 
            (key, list) => {
                lock(list) // Thread-safe list mod
                {
                    // Dedupe logic: Remove existing vote from same judge if any (update scenario)
                    var existing = list.FirstOrDefault(v => v.JudgeId == vote.JudgeId);
                    if (existing != null)
                        list.Remove(existing);
                    
                    list.Add(vote);
                }
                return list;
            });

        // 5. Notify UI
        // WeakReferenceMessenger.Default.Send(new VoteReceivedMessage(vote));
        // Note: Messenger is loosely coupled.
        try 
        {
            WeakReferenceMessenger.Default.Send(new VoteReceivedMessage(vote));
        }
        catch(Exception ex)
        {
            _logger.LogWarning("Failed to send UI message: {Error}", ex.Message);
            // Don't fail the operation just because UI didn't update
        }

        return Result.Success();
    }

    public List<Vote> GetAllVotes() => _votesByProject.Values.SelectMany(v => v).ToList();
    
    public Dictionary<string, double> GetProjectScores()
    {
        var scores = new Dictionary<string, double>();
        foreach(var kvp in _votesByProject)
        {
             scores[kvp.Key] = kvp.Value.Count; 
        }
        return scores;
    }
}

public class VoteReceivedMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<Vote>
{
    public VoteReceivedMessage(Vote vote) : base(vote) { }
}
