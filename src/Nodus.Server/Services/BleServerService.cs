using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Models;
using Nodus.Shared.Protocol;
using Nodus.Shared.Security;
using Nodus.Shared;
#if ANDROID
using Shiny.BluetoothLE.Hosting;
using Shiny.BluetoothLE;
#endif

namespace Nodus.Server.Services;

public class BleServerService
{
    // UUIDs sourced from NodusConstants (Shared)

#if ANDROID
    private readonly IBleHostingManager _bleHosting;
    private readonly ILogger<BleServerService> _logger;
    private IGattService? _gattService;
    private readonly Nodus.Shared.Services.ChunkerService.ChunkAssembler _assembler;
    private readonly Nodus.Shared.Services.DatabaseService _db;
    private readonly VoteAggregatorService _aggregator;
    
    // In-memory cache of current event key
    private byte[]? _currentEventAesKey;

    public BleServerService(IBleHostingManager bleHosting, Nodus.Shared.Services.DatabaseService db, VoteAggregatorService aggregator, ILogger<BleServerService> logger)
    {
        _bleHosting = bleHosting;
        _db = db;
        _aggregator = aggregator;
        _logger = logger;
        
        // Use new ChunkerService.ChunkAssembler
        _assembler = new Nodus.Shared.Services.ChunkerService.ChunkAssembler();
        
        _logger.LogInformation("BleServerService initialized");
        
        // Load active event key (simplified)
        Task.Run(async () => await LoadActiveEventKey());
    }

    // ... 

    public async Task StartAdvertisingAsync(string eventName)
    {
        // ... (standard start up)
        _logger.LogInformation("Starting BLE advertising for event: {EventName}", eventName);
        await LoadActiveEventKey(); // Refresh key on start
        
        if (_bleHosting.IsAdvertising) return;

        try
        {
            _gattService = await _bleHosting.AddService(NodusConstants.SERVICE_UUID, true, serviceBuilder =>
            {
                serviceBuilder.AddCharacteristic(NodusConstants.CHARACTERISTIC_UUID, cb => 
                {
                    cb.SetRead(request => Task.FromResult(GattResult.Success(Encoding.UTF8.GetBytes(eventName))));
                    
                    // Enable Notify
                    cb.SetNotification(async request => 
                    {
                         _logger.LogInformation("Client subscribed to notifications");
                         // Keep track of connected clients if needed
                    });

                    cb.SetWrite(async request =>
                    {
                        if (_assembler.Add(request.Data))
                        {
                            if (_assembler.IsComplete)
                            {
                                var payload = _assembler.GetPayload();
                                if (payload != null)
                                {
                                    OnPayloadReceived(payload);
                                }
                            }
                        }
                    });
                });
            });

            await _bleHosting.StartAdvertising(new AdvertisementOptions(
                LocalName: "Nodus Server",
                ServiceUuids: new[] { NodusConstants.SERVICE_UUID }
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BLE Hosting error");
        }
    }

    private void OnPayloadReceived(byte[] payload)
    {
        if (payload == null || payload.Length < 1) return;
        
        byte type = payload[0];

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // TYPE 0x01: JSON Packet (NodusPacket)
                if (type == NodusConstants.PACKET_TYPE_JSON)
                {
                    var jsonBytes = new byte[payload.Length - 1];
                    Array.Copy(payload, 1, jsonBytes, 0, jsonBytes.Length);
                    var json = Encoding.UTF8.GetString(jsonBytes);
                    await ProcessJsonPacketAsync(json);
                }
                // TYPE 0x02: Media Packet (VoteId + Image)
                else if (type == NodusConstants.PACKET_TYPE_MEDIA)
                {
                    await ProcessMediaPacketAsync(payload);
                }
                else
                {
                    _logger.LogWarning("Unknown payload type: {Type}", type);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payload");
            }
        });
    }

    private async Task ProcessMediaPacketAsync(byte[] payload)
    {
        // Structure: [0x02][VoteId(16)][ImageBytes...]
        if (payload.Length < 17) return;

        var voteIdBytes = new byte[16];
        Array.Copy(payload, 1, voteIdBytes, 0, 16);
        var voteId = new Guid(voteIdBytes).ToString();

        var imageBytes = new byte[payload.Length - 17];
        Array.Copy(payload, 17, imageBytes, 0, imageBytes.Length);

        _logger.LogInformation("Received Media for Vote {VoteId} ({Size} bytes)", voteId, imageBytes.Length);

        // 1. Verify Vote Exists
        var voteResult = await _db.GetVoteByIdAsync(voteId); 
        
        string targetFolder;
        if (voteResult.IsSuccess)
        {
            var vote = voteResult.Value;
            // Organize by EventId if possible
            targetFolder = Path.Combine(FileSystem.AppDataDirectory, "Media", vote.EventId);
        }
        else
        {
            // Fallback for orphaned media
            targetFolder = Path.Combine(FileSystem.AppDataDirectory, "Media", "Orphaned");
        }
        
        Directory.CreateDirectory(targetFolder);
        string fileName = $"{voteId}.jpg";
        string path = Path.Combine(targetFolder, fileName);
        
        await File.WriteAllBytesAsync(path, imageBytes);
        _logger.LogInformation("Saved media to {Path}", path);

        // 2. Update Vote Record if it exists
        if (voteResult.IsSuccess)
        {
            var vote = voteResult.Value;
            vote.LocalPhotoPath = path;
            vote.IsMediaSynced = true; // Mark as synced on server too (though meaningless here)
            await _db.SaveVoteAsync(vote);
            _logger.LogInformation("Updated Vote {VoteId} with media path", voteId);
            
            // Send ACK to Client
            await SendAckAsync(voteId);

            // Trigger UI update or aggregation?
            // _aggregator.ProcessVote(vote); // Optional re-process
        }
    }

    private async Task SendAckAsync(string voteId)
    {
        try
        {
            if (_gattService == null) return;
            
            // Format: [0xA1][VoteId (16 bytes)]
            if (Guid.TryParse(voteId, out var guid))
            {
                var payload = new byte[17];
                payload[0] = NodusConstants.PACKET_TYPE_ACK;
                Array.Copy(guid.ToByteArray(), 0, payload, 1, 16);
                
                // Notify all subscribed clients
                var characteristic = _gattService.Characteristics.FirstOrDefault(x => x.Uuid == NodusConstants.CHARACTERISTIC_UUID);
                if (characteristic != null)
                {
                    await characteristic.Notify(payload);
                    _logger.LogInformation("Sent ACK for Vote {VoteId}", voteId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send ACK for {VoteId}", voteId);
        }
    }

    private async Task ProcessJsonPacketAsync(string json)
    {
        var packet = NodusPacket.FromJson(json);
        if (packet == null) return;

        // Decrypt if necessary
        if (packet.EncryptedPayload != null && packet.EncryptedPayload.Length > 0 && _currentEventAesKey != null)
        {
             // ... existing decryption logic ...
             try 
             {
                 var decryptedBytes = CryptoHelper.Decrypt(packet.EncryptedPayload, _currentEventAesKey);
                 var decryptedJson = Encoding.UTF8.GetString(decryptedBytes);
                 
                 if (packet.Type == MessageType.Vote)
                 {
                      var vote = JsonSerializer.Deserialize<Vote>(decryptedJson);
                      if (vote != null)
                      {
                          vote.Status = SyncStatus.Synced;
                          vote.SyncedAtUtc = DateTime.UtcNow;
                          await _db.SaveVoteAsync(vote);
                          _aggregator.ProcessVote(vote);
                      }
                 }
             }
             catch (Exception decEx)
             {
                 _logger.LogWarning(decEx, "Decryption failed");
             }
        }
        else
        {
             // Unencrypted or Handshake
             // ...
        }
    }
    
    // ... (Constructor)

    public async Task LoadActiveEventKey()
    {
        var eventsResult = await _db.GetEventsAsync();
        if (eventsResult.IsFailure) return;
        
        var active = eventsResult.Value?.FirstOrDefault(e => e.IsActive);
        if (active != null && !string.IsNullOrEmpty(active.SharedAesKeyEncrypted))
        {
            try 
            {
                _currentEventAesKey = Convert.FromBase64String(active.SharedAesKeyEncrypted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load event AES key");
            }
        }
    }

    // Métodos duplicados eliminados - se mantienen solo las implementaciones originales arriba
#else
    private readonly ILogger<BleServerService> _logger;
    
    public BleServerService(Nodus.Shared.Services.DatabaseService db, ILogger<BleServerService> logger)
    {
        _logger = logger;
        _logger.LogWarning("BLE Server not available on this platform");
    }
    
    public Task StartAdvertisingAsync(string eventName)
    {
        _logger.LogWarning("StartAdvertising called but BLE is not supported on this platform");
        return Task.CompletedTask;
    }
    
    public void StopAdvertising()
    {
        _logger.LogDebug("StopAdvertising called but BLE is not supported on this platform");
    }
#endif
}
