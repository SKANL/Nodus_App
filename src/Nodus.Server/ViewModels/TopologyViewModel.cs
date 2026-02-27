using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Nodus.Shared.Models;
using Nodus.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Nodus.Server.ViewModels;

public partial class TopologyViewModel : ObservableObject, IDisposable
{
    private readonly TelemetryService _telemetryService;
    private readonly ILogger<TopologyViewModel> _logger;
    private System.Timers.Timer? _refreshTimer;

    // ── Observable State ───────────────────────────────────────────────────
    [ObservableProperty] public partial ObservableCollection<NetworkMetrics> Nodes { get; set; } = new();
    [ObservableProperty] public partial int TotalNodes { get; set; }
    [ObservableProperty] public partial int ActiveNodes { get; set; }
    [ObservableProperty] public partial int RelayNodes { get; set; }
    [ObservableProperty] public partial double AverageConnectionQuality { get; set; }
    [ObservableProperty] public partial string LastUpdateTime { get; set; } = "Nunca";

    public TopologyViewModel(TelemetryService telemetryService, ILogger<TopologyViewModel> logger)
    {
        _telemetryService = telemetryService;
        _logger = logger;

        // Subscribe to telemetry updates
        _telemetryService.TopologyUpdated += OnTopologyUpdated;
        _telemetryService.NodeMetricsUpdated += OnNodeMetricsUpdated;

        // Initial load
        RefreshTopology();

        // Auto-refresh every 2 seconds
        _refreshTimer = new System.Timers.Timer(2000);
        _refreshTimer.Elapsed += (s, e) => RefreshTopology();
        _refreshTimer.Start();
    }

    [RelayCommand]
    private void RefreshTopology()
    {
        try
        {
            var topology = _telemetryService.GetCurrentTopology();
            if (topology == null)
            {
                _logger.LogWarning("Topology data is currently unavailable.");
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                Nodes.Clear();
                foreach (var node in topology.Nodes.OrderByDescending(n => n.IsRelay).ThenBy(n => n.NodeLabel))
                {
                    Nodes.Add(node);
                }

                TotalNodes = topology.TotalNodes;
                ActiveNodes = topology.ActiveNodes;
                RelayNodes = topology.RelayNodes;
                AverageConnectionQuality = topology.AverageConnectionQuality;
                LastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh topology");
        }
    }

    private void OnTopologyUpdated(object? sender, NetworkTopology topology)
    {
        // Topology already updated via timer, but we could add specific handling here
    }

    private void OnNodeMetricsUpdated(object? sender, NetworkMetrics metrics)
    {
        // Update specific node in collection
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var existingNode = Nodes.FirstOrDefault(n => n.NodeId == metrics.NodeId);
            if (existingNode != null)
            {
                var index = Nodes.IndexOf(existingNode);
                Nodes[index] = metrics;
            }
            else
            {
                Nodes.Add(metrics);
            }
        });
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _telemetryService.TopologyUpdated -= OnTopologyUpdated;
        _telemetryService.NodeMetricsUpdated -= OnNodeMetricsUpdated;
    }
}
