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

    public List<ProjectLeaderboardEntry> GetLeaderboard()
    {
        var leaderboard = new List<ProjectLeaderboardEntry>();
        foreach(var kvp in _votesByProject)
        {
            var projectId = kvp.Key;
            var votes = kvp.Value;
            double totalScore = 0;
            int validVotes = 0;

            foreach(var vote in votes)
            {
                try 
                {
                    using var doc = JsonDocument.Parse(vote.PayloadJson);
                    if (doc.RootElement.TryGetProperty("CategoryScores", out var scoresArray))
                    {
                        double voteTotal = 0;
                        int catCount = 0;
                        foreach(var scoreObj in scoresArray.EnumerateArray())
                        {
                            if (scoreObj.TryGetProperty("Score", out var scoreVal))
                            {
                                voteTotal += scoreVal.GetDouble();
                                catCount++;
                            }
                        }
                        if (catCount > 0)
                        {
                            totalScore += (voteTotal / catCount);
                            validVotes++;
                        }
                    }
                } 
                catch { }
            }

            leaderboard.Add(new ProjectLeaderboardEntry 
            {
                ProjectId = projectId,
                AverageScore = validVotes > 0 ? (totalScore / validVotes) : 0,
                TotalVotes = votes.Count
            });
        }
        
        var sorted = leaderboard.OrderByDescending(x => x.AverageScore).ToList();
        
        // Assign ranks and colors
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Rank = i + 1;
            sorted[i].RankColor = (i + 1) switch
            {
                1 => "#FFD700", // Gold
                2 => "#C0C0C0", // Silver
                3 => "#CD7F32", // Bronze
                _ => "#94A3B8"  // Slate 400
            };
        }

        return sorted;
    }
}

public class VoteReceivedMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<Vote>
{
    public VoteReceivedMessage(Vote vote) : base(vote) { }
}

public class ProjectLeaderboardEntry
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public double AverageScore { get; set; }
    public int TotalVotes { get; set; }
    public int Rank { get; set; }
    public string RankColor { get; set; } = "#94A3B8";
}

