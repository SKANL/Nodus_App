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
    private readonly ChunkAssembler _assembler;
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
        _assembler = new ChunkAssembler();
        _assembler.PayloadCompleted += OnPayloadReceived;
        
        _logger.LogInformation("BleServerService initialized");
        
        // Load active event key (simplified)
        Task.Run(async () => await LoadActiveEventKey());
    }
    
    // ... (Constructor)

    public async Task LoadActiveEventKey()
    {
        var events = await _db.GetEventsAsync();
        var active = events.FirstOrDefault(e => e.IsActive);
        if (active != null && !string.IsNullOrEmpty(active.SharedAesKeyEncrypted))
        {
            try 
            {
                _currentEventAesKey = Convert.FromBase64String(active.SharedAesKeyEncrypted);
            }
            catch { /* Log Error */ }
        }
    }

    private void OnPayloadReceived(object? sender, byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            var packet = NodusPacket.FromJson(json);
            
            if (packet == null) return;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                // Decrypt if necessary
                if (packet.EncryptedPayload != null && packet.EncryptedPayload.Length > 0 && _currentEventAesKey != null)
                {
                    try 
                    {
                        var decryptedBytes = CryptoHelper.Decrypt(packet.EncryptedPayload, _currentEventAesKey);
                        var decryptedJson = Encoding.UTF8.GetString(decryptedBytes);
                        
                        // Handle Types
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
                        _logger.LogWarning(decEx, "Decryption failed for packet from {SenderId}", packet.SenderId);
                    }
                }
                else
                {
                     Application.Current?.MainPage?.DisplayAlert("Firefly Rx", $"{packet.Type} from {packet.SenderId} (Unencrypted/NoKey)", "OK");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing received payload");
        }
    }

    public async Task StartAdvertisingAsync(string eventName)
    {
        _logger.LogInformation("Starting BLE advertising for event: {EventName}", eventName);
        await LoadActiveEventKey(); // Refresh key on start
        
        if (_bleHosting.IsAdvertising)
        {
            _logger.LogDebug("Already advertising, skipping start");
            return;
        }

        try
        {
            _gattService = await _bleHosting.AddService(NodusConstants.SERVICE_UUID, true, serviceBuilder =>
            {
                serviceBuilder.AddCharacteristic(NodusConstants.CHARACTERISTIC_UUID, cb => 
                {
                    // cb.SetProperties(CharacteristicProperties.Read | CharacteristicProperties.Write | CharacteristicProperties.WriteWithoutResponse | CharacteristicProperties.Notify);  
                    
                    cb.SetRead(request => 
                    {
                         return Task.FromResult(GattResult.Success(Encoding.UTF8.GetBytes(eventName)));
                    });

                    cb.SetWrite(async request =>
                    {
                        _assembler.ProcessPacket(request.Data);
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
            _logger.LogError(ex, "BLE Hosting error while starting advertising");
        }
    }

    public void StopAdvertising()
    {
        _logger.LogInformation("Stopping BLE advertising");
        _bleHosting.StopAdvertising();
        if (_gattService != null)
        {
            _bleHosting.RemoveService(NodusConstants.SERVICE_UUID);
            _gattService = null;
        }
    }
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
