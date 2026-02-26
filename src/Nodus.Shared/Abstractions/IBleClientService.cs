using Nodus.Shared.Common;
using Nodus.Shared.Models;
using Nodus.Shared.Protocol;

namespace Nodus.Shared.Abstractions;

/// <summary>
/// Abstraction for BLE client operations with explicit error handling and cancellation support.
/// </summary>
public interface IBleClientService
{
    /// <summary>
    /// Current connection state observable.
    /// </summary>
    IObservable<string> ConnectionState { get; }
    
    /// <summary>
    /// Indicates if currently connected to a server or relay.
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Last measured RSSI value.
    /// </summary>
    int LastRssi { get; }

    /// <summary>
    /// Notifications received from the server (e.g., ACKs).
    /// </summary>
    IObservable<byte[]> Notifications { get; }

    /// <summary>
    /// Enables notifications on the characteristic.
    /// </summary>
    Task<Result> EnableNotificationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the current RSSI value.
    /// </summary>
    Task<int> ReadRssiAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Event raised when a server/relay is discovered during scanning.
    /// </summary>
    event EventHandler<string>? ServerDiscovered;
    
    /// <summary>
    /// Event raised when connection status changes.
    /// </summary>
    event EventHandler<bool>? ConnectionStatusChanged;
    
    /// <summary>
    /// Event raised when nearby link count changes (for Trickle algorithm).
    /// </summary>
    event EventHandler<int>? LinkCountChanged;

    /// <summary>
    /// Starts scanning for Nodus servers and relays.
    /// </summary>
    Task<Result> StartScanningForServerAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Stops scanning for servers.
    /// </summary>
    void StopScanning();
    
    /// <summary>
    /// Connects to a discovered peripheral with timeout.
    /// </summary>
    Task<Result> ConnectAsync(IBlePeripheralWrapper peripheral, TimeSpan? timeout = null, CancellationToken ct = default);
    
    /// <summary>
    /// Fetches the project catalog from the connected server via BLE stream.
    /// </summary>
    Task<Result<List<Project>>> GetProjectsFromServerAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Sends a vote packet with encryption and signing.
    /// </summary>
    Task<Result> SendVoteAsync(Vote vote, CancellationToken ct = default);
    
    /// <summary>
    /// Relays a packet without modification (for relay nodes).
    /// </summary>
    Task<Result> RelayPacketAsync(NodusPacket packet, CancellationToken ct = default);
    
    /// <summary>
    /// Writes raw data to the characteristic (for chunked media transfer).
    /// </summary>
    Task<Result> WriteRawAsync(byte[] data, CancellationToken ct = default);
    
    /// <summary>
    /// Disconnects from the current peripheral.
    /// </summary>
    Task<Result> DisconnectAsync(CancellationToken ct = default);
}
