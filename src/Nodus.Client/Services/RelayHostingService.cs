using System.Text;
using Microsoft.Extensions.Logging;
using Nodus.Shared;
using Nodus.Shared.Protocol;
using Nodus.Shared.Common;
#if ANDROID
using Shiny.BluetoothLE.Hosting;
#endif


using Nodus.Shared.Abstractions;
using Nodus.Infrastructure.Services;

namespace Nodus.Client.Services;



public class RelayHostingService : IRelayHostingService
{
    // Shared Logic
    private readonly PacketTracker _packetTracker; // Singleton injected
    private readonly IBleClientService _upstreamClient;

#if ANDROID
    private readonly IBleHostingManager _bleHosting;
    private readonly ILogger<RelayHostingService> _logger;
    private IGattService? _gattService;
    private readonly ChunkAssembler _assembler;

    /// <summary>
    /// Unique identifier for this relay node (runtime-scoped).
    /// Used in hop-trace to detect routing loops: packet is dropped if this ID is already in Hops.
    /// </summary>
    private readonly string _relayNodeId = Guid.NewGuid().ToString("N");

    public RelayHostingService(IBleHostingManager bleHosting, IBleClientService upstreamClient, PacketTracker packetTracker, ILogger<RelayHostingService> logger)
    {
        _bleHosting = bleHosting;
        _upstreamClient = upstreamClient;
        _packetTracker = packetTracker;
        _logger = logger;
        _assembler = new ChunkAssembler();
        _assembler.PayloadCompleted += OnPayloadReceived;

        _logger.LogInformation("RelayHostingService initialized");
    }

    private async void OnPayloadReceived(object? sender, byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            var packet = NodusPacket.FromJson(json);

            if (packet == null) return;

            // --- Loop Prevention (PacketTracker) ---
            // If TryProcess returns false, this relay has already processed this packet -> DROP.
            if (!_packetTracker.TryProcess(packet.Id))
            {
                _logger.LogDebug("Dropped duplicate packet {PacketId} from {SenderId}", packet.Id, packet.SenderId);
                return;
            }

            // --- Hop-Trace Loop Detection (doc 02, doc 12) ---
            // Drop if our own node ID is already in the trace (would cause a loop).
            if (packet.Hops.Contains(_relayNodeId))
            {
                _logger.LogWarning(
                    "Dropped looping packet {PacketId}: relay {NodeId} already in hops [{Hops}]",
                    packet.Id, _relayNodeId, string.Join(",", packet.Hops));
                return;
            }

            // --- TTL Enforcement (doc 02 §MAX_HOPS_TTL = 2) ---
            // Decrement before forwarding. Drop if TTL hits 0.
            if (packet.Ttl <= 0)
            {
                _logger.LogWarning("Dropped packet {PacketId}: TTL exhausted", packet.Id);
                return;
            }
            packet.Ttl--;

            // Record ourselves in the hop trace so the next relay can detect our presence.
            packet.Hops.Add(_relayNodeId);

            _logger.LogInformation(
                "Relaying packet {PacketType} from {SenderId} (TTL={Ttl}, Hops={HopCount})",
                packet.Type, packet.SenderId, packet.Ttl, packet.Hops.Count);

            // Forward to Upstream (Server) without re-encryption.
            await _upstreamClient.RelayPacketAsync(packet);

            _logger.LogInformation("Packet {PacketId} forwarded to upstream", packet.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing relay payload");
        }
    }

    public bool IsAdvertising => _bleHosting?.IsAdvertising ?? false;

    public async Task<Result> StartAdvertisingAsync(CancellationToken ct = default)
    {
        if (_bleHosting.IsAdvertising) return Result.Success();

        try
        {
            // 1. Add Service (Same UUID as Server so Seekers find us)
            _gattService = await _bleHosting.AddService(NodusConstants.SERVICE_UUID, true, sb =>
            {
                sb.AddCharacteristic(NodusConstants.CHARACTERISTIC_UUID, cb =>
                {
                    // cb.SetProperties(CharacteristicProperties.WriteWithoutResponse);
                    cb.SetWrite(request =>
                    {
                        _assembler.ProcessPacket(request.Data);
                        return Task.CompletedTask;
                    });
                });
            });

            // 2. Advertise with Nodus Relay name and Service UUID.
            // NOTE: ManufacturerData (Byte0=0x02 relay indicator, Byte1=battery%) is defined
            // in doc 02 but Shiny.BluetoothLE.Hosting 3.3.4 AdvertisementOptions does NOT
            // expose a ManufacturerData parameter. Seekers cannot use battery-preference
            // until this API is available in a future Shiny version or via a custom Android
            // BLE advertising workaround.
            await _bleHosting.StartAdvertising(new AdvertisementOptions(
                LocalName: "Nodus Relay",
                ServiceUuids: new[] { NodusConstants.SERVICE_UUID }
            ));

            _logger.LogInformation("Started advertising as Relay");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting relay advertising");
            return Result.Failure("Error starting relay advertising", ex);
        }
    }

    public void StopAdvertising()
    {
        _logger.LogInformation("Stopping relay advertising");
        _bleHosting.StopAdvertising();
        if (_gattService != null)
        {
            _bleHosting.RemoveService(NodusConstants.SERVICE_UUID);
            _gattService = null;
        }
    }
#else
    private readonly ILogger<RelayHostingService> _logger;
    
    public bool IsAdvertising => false;

    public RelayHostingService(IBleClientService upstreamClient, PacketTracker packetTracker, ILogger<RelayHostingService> logger) 
    {
        _upstreamClient = upstreamClient;
        _packetTracker = packetTracker;
        _logger = logger;
        _logger.LogWarning("Relay hosting not available on this platform");
    }
    
    public Task<Result> StartAdvertisingAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("StartAdvertising called but relay hosting is not supported on this platform");
        return Task.FromResult(Result.Success());
    }
    
    public void StopAdvertising()
    {
        _logger.LogDebug("StopAdvertising called but relay hosting is not supported on this platform");
    }
#endif
}
