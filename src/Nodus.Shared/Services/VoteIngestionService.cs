using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nodus.Shared.Abstractions;
using Nodus.Shared.Models;
using Nodus.Shared.Protocol;
using Nodus.Shared.Security;

namespace Nodus.Shared.Services;

public class VoteIngestionService
{
    private readonly IDatabaseService _db;
    private readonly VoteAggregatorService _aggregator;
    private readonly IFileService _fileService;
    private readonly ILogger<VoteIngestionService> _logger;
    private byte[]? _currentEventAesKey;

    public VoteIngestionService(IDatabaseService db, VoteAggregatorService aggregator, IFileService fileService, ILogger<VoteIngestionService> logger)
    {
        _db = db;
        _aggregator = aggregator;
        _fileService = fileService;
        _logger = logger;
    }

    public void SetEventAesKey(byte[] key)
    {
        _currentEventAesKey = key;
    }

    public async Task<byte[]?> ProcessPayloadAsync(byte[] payload)
    {
        if (payload == null || payload.Length < 1) return null;
        
        byte type = payload[0];

        try
        {
            if (type == NodusConstants.PACKET_TYPE_JSON)
            {
                var jsonBytes = new byte[payload.Length - 1];
                Array.Copy(payload, 1, jsonBytes, 0, jsonBytes.Length);
                var json = Encoding.UTF8.GetString(jsonBytes);
                await ProcessJsonPacketAsync(json);
                return null; // No immediate response for JSON usually, or maybe Ack?
            }
            else if (type == NodusConstants.PACKET_TYPE_MEDIA)
            {
                return await ProcessMediaPacketAsync(payload);
            }
            else
            {
                _logger.LogWarning("Unknown payload type: {Type}", type);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payload");
            return null;
        }
    }

    private async Task<byte[]?> ProcessMediaPacketAsync(byte[] payload)
    {
        // Structure: [0x02][VoteId(16)][ImageBytes...]
        if (payload.Length < 17) return null;

        var voteIdBytes = new byte[16];
        Array.Copy(payload, 1, voteIdBytes, 0, 16);
        var voteId = new Guid(voteIdBytes).ToString();

        var imageBytes = new byte[payload.Length - 17];
        Array.Copy(payload, 17, imageBytes, 0, imageBytes.Length);

        _logger.LogInformation("Received Media for Vote {VoteId} ({Size} bytes)", voteId, imageBytes.Length);

        // 1. Verify Vote Exists
        var voteResult = await _db.GetVoteByIdAsync(voteId); 
        
        string targetFolder;
        // Simplified Logic: We need to store it regardless
        if (voteResult.IsSuccess && voteResult.Value != null)
        {
            var vote = voteResult.Value;
            var eventId = string.IsNullOrEmpty(vote.EventId) ? "Unknown" : vote.EventId;
            // Note: FileSystem.AppDataDirectory is MAUI specific. 
            // Shared project should arguably abstract the ROOT path too, but for now we rely on simple paths or IFileService handling it?
            // Since IFileService implementation in Shared uses File APIs, it expects full paths.
            // We need a path provider.
            // But let's assume we pass relative paths or IFileService knows root?
            // No, the original code used FileSystem.AppDataDirectory.
            // We should inject IPathProvider or similar.
            // For now, let's use a hardcoded "Media" folder relative to execution or similar?
            // Or better: Let IFileService have `GetAppDataDirectory()`.
            // Let's stick to the original logic but we need the path.
            // I'll add `GetAppDataPath()` to `IFileService`?
            // Or just use `System.Environment.GetFolderPath(...)`.
            
            // Let's use a safe default for now.
            targetFolder = Path.Combine(_fileService.GetAppDataDirectory(), "Media", eventId);
        }
        else
        {
            targetFolder = Path.Combine(_fileService.GetAppDataDirectory(), "Media", "Orphaned");
        }
        
        _fileService.CreateDirectory(targetFolder);
        string fileName = $"{voteId}.jpg";
        string path = Path.Combine(targetFolder, fileName);
        
        await _fileService.WriteAllBytesAsync(path, imageBytes);
        _logger.LogInformation("Saved media to {Path}", path);

        // 2. Update Vote Record if it exists
        if (voteResult.IsSuccess && voteResult.Value != null)
        {
            var vote = voteResult.Value;
            vote.LocalPhotoPath = path;
            vote.IsMediaSynced = true;
            await _db.SaveVoteAsync(vote);
            _logger.LogInformation("Updated Vote {VoteId} with media path", voteId);
            
            // Return ACK payload
            return CreateAckPayload(voteId);
        }
        
        return null;
    }

    private byte[] CreateAckPayload(string voteId)
    {
        if (Guid.TryParse(voteId, out var guid))
        {
            var payload = new byte[17];
            payload[0] = NodusConstants.PACKET_TYPE_ACK;
            Array.Copy(guid.ToByteArray(), 0, payload, 1, 16);
            return payload;
        }
        return Array.Empty<byte>();
    }

    private async Task ProcessJsonPacketAsync(string json)
    {
        var packet = NodusPacket.FromJson(json);
        if (packet == null) return;

        if (packet.EncryptedPayload != null && packet.EncryptedPayload.Length > 0 && _currentEventAesKey != null)
        {
             try 
             {
                 var decryptedBytes = CryptoHelper.Decrypt(packet.EncryptedPayload, _currentEventAesKey);
                 var decryptedJson = Encoding.UTF8.GetString(decryptedBytes);
                 
                 if (packet.Type == MessageType.Vote)
                 {
                      var vote = JsonSerializer.Deserialize<Vote>(decryptedJson);
                      if (vote != null)
                      {
                          vote.SyncedAtUtc = DateTime.UtcNow;
                          await _db.SaveVoteAsync(vote);
                          await _aggregator.ProcessVoteAsync(vote);
                      }
                 }
             }
             catch (Exception decEx)
             {
                 _logger.LogWarning(decEx, "Decryption failed");
             }
        }
    }
}
