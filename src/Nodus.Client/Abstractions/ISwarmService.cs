using System.ComponentModel;
using Nodus.Client.Services;

namespace Nodus.Client.Abstractions;

public interface ISwarmService : INotifyPropertyChanged
{
    SwarmState CurrentState { get; }
    int NeighborLinkCount { get; }
    bool IsMuleMode { get; }
    void UpdateNeighborStats(int linkCount);
}
