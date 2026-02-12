using System.Text;
using Microsoft.Extensions.Logging;
using Nodus.Shared;
using Nodus.Shared.Protocol;
using Nodus.Shared.Common;
#if ANDROID
using Shiny.BluetoothLE.Hosting;
#endif


using Nodus.Shared.Abstractions;

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
            
            // Loop Prevention (PacketTracker)
            // If TryProcess returns false, we have seen this packet recently -> DROP IT.
            if (!_packetTracker.TryProcess(packet.Id))
            {
                _logger.LogDebug("Dropped duplicate packet {PacketId} from {SenderId}", packet.Id, packet.SenderId);
                return;
            }
            
            _logger.LogInformation("Relaying packet {PacketType} from {SenderId}", packet.Type, packet.SenderId);
            
            // Forward to Upstream (Server) without modification
            // We use the new RelayPacketAsync method which sends raw bytes without re-encryption.
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

            // 2. Advertise with MANUFACTURER DATA = 0x02 (Relay)
            await _bleHosting.StartAdvertising(new AdvertisementOptions(
                LocalName: "Nodus Relay",
                ServiceUuids: new[] { NodusConstants.SERVICE_UUID }
                // ManufacturerData: Shiny API valid check needed
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
