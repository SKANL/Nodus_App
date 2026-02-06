using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Nodus.Server.Services;
using Nodus.Shared.Models;
using System.Collections.ObjectModel;

namespace Nodus.Server.ViewModels;

public partial class ResultsViewModel : ObservableObject, IRecipient<VoteReceivedMessage>
{
    private readonly VoteAggregatorService _aggregator;
    
    public ObservableCollection<Vote> Votes { get; } = new();

    public ResultsViewModel(VoteAggregatorService aggregator)
    {
        _aggregator = aggregator;
        
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
}
