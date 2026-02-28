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
    private readonly Nodus.Shared.Abstractions.IChunkerService _chunker;
    private System.Timers.Timer? _projectsSyncTimer;

    public BleServerService(IBleHostingManager bleHosting, VoteIngestionService ingestion, Nodus.Shared.Abstractions.IDatabaseService db, Nodus.Shared.Abstractions.IChunkerService chunker, ILogger<BleServerService> logger)
    {
        _bleHosting = bleHosting;
        _ingestion = ingestion;
        _db = db;
        _chunker = chunker;
        _assembler = new Nodus.Shared.Services.ChunkerService.ChunkAssembler();
        _logger = logger;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await LoadActiveEventKey();
            await SyncProjectsFromDbAsync();
            StartSyncTimer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BLE server initialization failed");
        }
    }

    private void StartSyncTimer()
    {
        // Poll DB every 30 seconds for new projects from Web
        if (_projectsSyncTimer != null) return;

        _projectsSyncTimer = new System.Timers.Timer(30000);
        _projectsSyncTimer.Elapsed += async (s, e) => await SyncProjectsFromDbAsync();
        _projectsSyncTimer.Start();
    }

    private List<Project> _activeProjects = new();

    public async Task SyncProjectsFromDbAsync()
    {
        try
        {
            var result = await _db.GetAllProjectsAsync();
            if (result.IsSuccess)
            {
                _activeProjects = result.Value;
                _logger.LogInformation("Synced {Count} projects from DB", _activeProjects.Count);

                // If we have connected clients, we could notify them of the change
                // or just let them read the latest list.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync projects from DB");
        }
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
                // Characteristic for Project Discovery
                serviceBuilder.AddCharacteristic("00002A01-0000-1000-8000-00805F9B34FB", cb =>
                {
                    cb.SetRead(request =>
                    {
                        var json = JsonSerializer.Serialize(_activeProjects);
                        return Task.FromResult(GattResult.Success(Encoding.UTF8.GetBytes(json)));
                    });

                    cb.SetNotification(async request =>
                    {
                        try
                        {
                            _logger.LogInformation("Client subscribed to Project Sync. Preparing stream...");
                            var json = JsonSerializer.Serialize(_activeProjects);
                            var jsonBytes = Encoding.UTF8.GetBytes(json);

                            var payload = new byte[jsonBytes.Length + 1];
                            payload[0] = NodusConstants.PACKET_TYPE_PROJECTS;
                            Array.Copy(jsonBytes, 0, payload, 1, jsonBytes.Length);

                            var chunks = _chunker.Split(payload, 0xFF); // Message ID 255
                            _logger.LogInformation("Streaming {Count} chunks...", chunks.Count);

                            foreach (var chunk in chunks)
                            {
                                await request.Characteristic.Notify(chunk);
                            }
                            _logger.LogInformation("Project stream complete.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to stream projects");
                        }
                    });
                });

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
