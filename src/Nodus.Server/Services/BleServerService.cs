using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Models;
using Nodus.Shared.Protocol;
using Nodus.Shared.Security;
using Nodus.Shared;
using Nodus.Shared.Services; // For VoteAggregatorService
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
    private readonly VoteIngestionService _ingestion;
    private readonly Nodus.Shared.Abstractions.IDatabaseService _db;

    public BleServerService(IBleHostingManager bleHosting, VoteIngestionService ingestion, Nodus.Shared.Abstractions.IDatabaseService db, ILogger<BleServerService> logger)
    {
        _bleHosting = bleHosting;
        _ingestion = ingestion;
        _db = db;
        _logger = logger;
        
        // Use new ChunkerService.ChunkAssembler
        _assembler = new ChunkerService.ChunkAssembler();
        
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
                                    var response = await _ingestion.ProcessPayloadAsync(payload);
                                    if (response != null)
                                    {
                                        // Send response (ACK) via Notify
                                        if (_gattService != null) 
                                        {
                                            var characteristic = _gattService.Characteristics.FirstOrDefault(x => x.Uuid == NodusConstants.CHARACTERISTIC_UUID);
                                            if (characteristic != null)
                                            {
                                                await characteristic.Notify(response);
                                                _logger.LogInformation("Sent ACK for payload");
                                            }
                                        }
                                    }
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

    // Logic moved to VoteIngestionService
    
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
                var key = Convert.FromBase64String(active.SharedAesKeyEncrypted);
                _ingestion.SetEventAesKey(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load event AES key");
            }
        }
    }

#else

    private readonly ILogger<BleServerService> _logger;
    private readonly VoteIngestionService _ingestion;

    public BleServerService(VoteIngestionService ingestion, ILogger<BleServerService> logger)
    {
        _ingestion = ingestion;
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
