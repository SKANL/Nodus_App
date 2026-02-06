using CommunityToolkit.Mvvm.Messaging;
using Nodus.Shared.Models;
using System.Collections.Concurrent;

namespace Nodus.Server.Services;

public class VoteAggregatorService
{
    private readonly ConcurrentDictionary<string, List<Vote>> _votesByProject = new();
    
    // Simple aggregator that could calculate averages
    public void ProcessVote(Vote vote)
    {
        _votesByProject.AddOrUpdate(vote.ProjectId, 
            new List<Vote> { vote }, 
            (key, list) => {
                // Dedupe if needed, but for now just appending
                if (!list.Any(v => v.JudgeId == vote.JudgeId)) 
                    list.Add(vote);
                return list;
            });

        // Notify UI via Messenger
        WeakReferenceMessenger.Default.Send(new VoteReceivedMessage(vote));
    }

    public List<Vote> GetAllVotes() => _votesByProject.Values.SelectMany(v => v).ToList();
    
    public Dictionary<string, double> GetProjectScores()
    {
        // Example: Only counting Design Score average for MVP
        var scores = new Dictionary<string, double>();
        foreach(var kvp in _votesByProject)
        {
             // naive avg
             // In reality need to parse PayloadJson
             scores[kvp.Key] = kvp.Value.Count; // Just vote count for now
        }
        return scores;
    }
}

public class VoteReceivedMessage : CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<Vote>
{
    public VoteReceivedMessage(Vote vote) : base(vote) { }
}
