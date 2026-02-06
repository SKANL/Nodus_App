using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Nodus.Shared.Models; // Assuming Node/Peer models exist or defining simple ones here

namespace Nodus.Server.ViewModels;

public partial class TopologyViewModel : ObservableObject
{
    public enum NodeStatus
    {
        Online, // Green
        Warning, // Yellow (Weak signal or stale)
        Offline // Red
    }

    // Minimal Node representation for UI
    public class NodeModel : ObservableObject
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsRelay { get; set; }
        public DateTime LastSeen { get; set; }
        public int Rssi { get; set; }

        public NodeStatus Status
        {
            get
            {
                var timeSinceLastSeen = DateTime.Now - LastSeen;
                if (timeSinceLastSeen.TotalSeconds > 30) return NodeStatus.Offline;
                if (timeSinceLastSeen.TotalSeconds > 10 || Rssi < -85) return NodeStatus.Warning;
                return NodeStatus.Online;
            }
        }
    }

    public ObservableCollection<NodeModel> Nodes { get; } = new();

    public TopologyViewModel()
    {
        // Mock Data for Visualization
        Nodes.Add(new NodeModel { Id = "SVR", Label = "Server (You)", IsRelay = true, LastSeen = DateTime.Now, Rssi = -50 });
        Nodes.Add(new NodeModel { Id = "J1", Label = "Judge 1", IsRelay = false, LastSeen = DateTime.Now, Rssi = -60 });
        Nodes.Add(new NodeModel { Id = "J2", Label = "Judge 2", IsRelay = true, LastSeen = DateTime.Now.AddSeconds(-15), Rssi = -75 }); // Warning (Stale)
        Nodes.Add(new NodeModel { Id = "J3", Label = "Judge 3", IsRelay = false, LastSeen = DateTime.Now.AddSeconds(-40), Rssi = -80 }); // Offline
    }
}
