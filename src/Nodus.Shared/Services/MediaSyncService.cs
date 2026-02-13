using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

using Nodus.Shared;
using Nodus.Shared.Models;
using Nodus.Shared.Services;
using Nodus.Shared.Abstractions;

namespace Nodus.Shared.Services;

public class MediaSyncService
{
    private readonly IBleClientService _bleService;
    private readonly IDatabaseService _databaseService; 
    private readonly IChunkerService _chunker;
    private readonly IImageCompressionService _compressor;
    private readonly IFileService _fileService;
    private readonly ILogger<MediaSyncService> _logger;
    private bool _isSyncing;
    private const int RssiThreshold = -75; // Lowered to improve sync probability
    private CancellationTokenSource? _rssiCts;

    public bool IsConnected => _bleService.IsConnected;
    
    // Store pending ACKs: VoteId -> TaskCompletionSource
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingAcks = new();

    // Configurable timeout for testing
    public TimeSpan AckTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public event EventHandler<string>? SyncStatusChanged;
    public event EventHandler<double>? SyncProgressChanged;

    public MediaSyncService(
        IBleClientService bleService, 
        IDatabaseService databaseService,
        IChunkerService chunker,
        IImageCompressionService compressor,
        IFileService fileService,
        ILogger<MediaSyncService> logger)
    {
        _bleService = bleService;
        _databaseService = databaseService;
        _chunker = chunker;
        _compressor = compressor;
        _fileService = fileService;
        _logger = logger;
        
        _bleService.ConnectionState.Subscribe(OnConnectionStateChanged);
        _bleService.Notifications.Subscribe(OnNotificationReceived);
    }
    
    // ...

    private void OnNotificationReceived(byte[] data)
    {
        if (data == null || data.Length < 17) return;

        // ACK Packet: [0xA1][VoteId(16)]
        if (data[0] == NodusConstants.PACKET_TYPE_ACK)
        {
            var voteIdBytes = new byte[16];
            Array.Copy(data, 1, voteIdBytes, 0, 16);
            var voteId = new Guid(voteIdBytes).ToString();

            if (_pendingAcks.TryRemove(voteId, out var tcs))
            {
                tcs.TrySetResult(true);
                _logger.LogInformation("Received ACK for vote {Id}", voteId);
            }
        }
    }

    private void OnConnectionStateChanged(Shiny.BluetoothLE.ConnectionState state)
    {
        if (state == Shiny.BluetoothLE.ConnectionState.Connected)
        {
            // Enable Notifications for ACKs
            _ = _bleService.EnableNotificationsAsync();

            // Start Active RSSI Monitoring
            _rssiCts?.Cancel();
            _rssiCts = new CancellationTokenSource();
            _ = MonitorRssiLoopAsync(_rssiCts.Token);
        }
        else
        {
            _rssiCts?.Cancel();
            _rssiCts = null;
        }
    }
    
    private async Task MonitorRssiLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _bleService.IsConnected)
        {
            try
            {
                int rssi = await _bleService.ReadRssiAsync(ct);
                // _logger.LogDebug("Current RSSI: {Rssi}", rssi);

                if (rssi > RssiThreshold && !_isSyncing)
                {
                    await CheckAndSyncAsync(rssi);
                }

                await Task.Delay(3000, ct); // Poll every 3 seconds
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("Error in RSSI monitor loop: {Error}", ex.Message);
            }
        }
    }

    public event EventHandler<bool>? SyncStateChanged;

    // Explicit trigger method (e.g. called by a timer or UI Action)
    public async Task CheckAndSyncAsync(int currentRssi = -999)
    {
        if (_isSyncing || !_bleService.IsConnected) return; // Prevent concurrent syncs
        
        // Use provided RSSI or LastRssi
        int rssi = currentRssi != -999 ? currentRssi : _bleService.LastRssi;

        // Strict Check: RSSI > -60 (or threshold)
        if (rssi < RssiThreshold) 
        {
             _logger.LogDebug("Signal too weak for Media Sync ({Rssi} < {Threshold})", rssi, RssiThreshold);
             return; 
        }

        try
        {
            _isSyncing = true;
            SyncStateChanged?.Invoke(this, true);
            await SyncPendingMediaAsync();
        }
        finally
        {
            _isSyncing = false;
            SyncStateChanged?.Invoke(this, false);
        }
    }

    private async Task SyncPendingMediaAsync()
    {
        var result = await _databaseService.GetVotesWithPendingMediaAsync();
        if (result.IsFailure)
        {
             _logger.LogError("Failed to get pending media votes: {Error}", result.Error);
             return;
        }
        
        var pendingVotes = result.Value;
        if (pendingVotes == null || !pendingVotes.Any()) return;

        _logger.LogInformation("Starting Mule Mode Sync for {Count} items", pendingVotes.Count);

        foreach (var vote in pendingVotes)
        {
            if (string.IsNullOrEmpty(vote.LocalPhotoPath) || !_fileService.Exists(vote.LocalPhotoPath))
            {
                // Mark as synced? Or error?
                _logger.LogWarning("Missing file for vote {Id}", vote.Id);
                continue;
            }

            try
            {
                byte[] originalBytes = await _fileService.ReadAllBytesAsync(vote.LocalPhotoPath);
                
                // Compress
                byte[] imageBytes = _compressor.Compress(originalBytes);
                
                // Construct Payload: [0x02 (Type)][VoteId (16)][ImageBytes...]
                var voteIdBytes = Guid.Parse(vote.Id).ToByteArray();
                var payload = new byte[1 + 16 + imageBytes.Length];
                
                payload[0] = 0x02; // Media Type
                Array.Copy(voteIdBytes, 0, payload, 1, 16);
                Array.Copy(imageBytes, 0, payload, 17, imageBytes.Length);

                byte msgId = (byte)(vote.Id.GetHashCode() & 0xFF); 
                
                var chunks = _chunker.Split(payload, msgId);
                
                _logger.LogInformation("Sending photo for vote {Id}: {Size} bytes, {Chunks} chunks", vote.Id, imageBytes.Length, chunks.Count);

                foreach (var chunk in chunks)
                {
                    // Use WriteRawAsync which wraps WriteCharacteristic
                    var writeResult = await _bleService.WriteRawAsync(chunk);
                    if (!writeResult.IsSuccess)
                    {
                        throw new Exception($"Failed to send chunk: {writeResult.Error}");
                    }
                    
                    await Task.Delay(20); 
                }

                // Wait for ACK
                var tcs = new TaskCompletionSource<bool>();
                _pendingAcks[vote.Id] = tcs;

                // Timeout
                var timeoutTask = Task.Delay(AckTimeout);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _pendingAcks.TryRemove(vote.Id, out _);
                    SyncStatusChanged?.Invoke(this, $"Timeout waiting for ACK on vote {vote.Id}");
                    throw new TimeoutException("Timed out waiting for ACK");
                }

                // ACK Received
                vote.IsMediaSynced = true;
                await _databaseService.SaveVoteAsync(vote);
                
                _logger.LogInformation("Synced media for vote {Id}", vote.Id);
                SyncStatusChanged?.Invoke(this, $"Synced media for vote {vote.Id}");
                
                // Calculate and report progress
                double progress = (double)(pendingVotes.IndexOf(vote) + 1) / pendingVotes.Count;
                SyncProgressChanged?.Invoke(this, progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync media for vote {Id}", vote.Id);
                SyncStatusChanged?.Invoke(this, $"Failed to sync vote {vote.Id}: {ex.Message}");
                // Break or continue? Continue to try next.
            }
        }
        
        SyncStatusChanged?.Invoke(this, "Sync complete");
        _isSyncing = false;
    }
}
