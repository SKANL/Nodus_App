using System.Diagnostics;
using System.Reactive.Linq;
using Microsoft.Extensions.Logging;
using Nodus.Shared;
using Nodus.Shared.Common;
using Nodus.Shared.Protocol;
using Nodus.Shared.Security;
using Nodus.Shared.Services; // For ISecureStorageService
using Nodus.Shared.Abstractions; // For IBleClientService
using Shiny.BluetoothLE;

namespace Nodus.Shared.Services;

/// <summary>
/// Professional BLE Client Service with CancellationToken support, timeout handling,
/// connection state machine, and retry logic with exponential backoff.
/// Implements IBleClientService for testability.
/// </summary>
public class BleClientService : IBleClientService, IDisposable
{
    private readonly IBleManager _bleManager;
    private readonly ILogger<BleClientService> _logger;
    private readonly ISecureStorageService _secureStorage;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    
    private IDisposable? _scanSubscription;
    private IBlePeripheralWrapper? _connectedServer;
    private CancellationTokenSource? _connectionCts;
    private IDisposable? _notificationSubscription;
    
    // Neighbor Discovery (Trickle)
    private readonly Dictionary<string, DateTime> _nearbyLinks = new();
    private readonly TimeSpan _neighborTtl = TimeSpan.FromSeconds(15);
    
    // Retry Configuration
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    
    public event EventHandler<string>? ServerDiscovered;
    public event EventHandler<bool>? ConnectionStatusChanged;
    public event EventHandler<int>? LinkCountChanged;

    private readonly ChunkerService _chunker;

    public BleClientService(IBleManager bleManager, ChunkerService chunker, ISecureStorageService secureStorage, ILogger<BleClientService> logger)
    {
        _bleManager = bleManager;
        _chunker = chunker;
        _secureStorage = secureStorage;
        _logger = logger;
        _logger.LogInformation("BleClientService initialized");
    }

    // New Notifications Observable (Subject needed to push values)
    private readonly System.Reactive.Subjects.Subject<byte[]> _notificationsSubject = new();
    public IObservable<byte[]> Notifications => _notificationsSubject.AsObservable();

    public async Task<Result> EnableNotificationsAsync(CancellationToken ct = default)
    {
        if (_connectedServer == null) return Result.Failure("Not connected");

        try
        {
            // Subscribe to notifications using Shiny extension method pattern
            _notificationSubscription?.Dispose();
            _notificationSubscription = _connectedServer
                .NotifyCharacteristic(NodusConstants.SERVICE_UUID, NodusConstants.CHARACTERISTIC_UUID)
                .Subscribe(
                    result => 
                    {
                        if (result.Data != null)
                        {
                            _notificationsSubject.OnNext(result.Data);
                        }
                    },
                    ex => _logger.LogError(ex, "Notification error")
                );

            _logger.LogInformation("Notifications enabled for characteristic {CharUuid}", NodusConstants.CHARACTERISTIC_UUID);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable notifications");
            return Result.Failure("Failed to enable notifications", ex);
        }
    }

    public async Task<int> ReadRssiAsync(CancellationToken ct = default)
    {
        if (_connectedServer == null) return -999;
        try
        {
            var rssi = await _connectedServer.ReadRssiAsync(ct);
            LastRssi = rssi;
            return rssi;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read RSSI");
            return LastRssi;
        }
    }

    public void CheckState()
    {
        // For testing/debugging state
    }

    private async Task<Result> TransmitPacketAsync(NodusPacket packet, CancellationToken ct = default, IBlePeripheralWrapper? overridePeripheral = null)
    {
        var target = overridePeripheral ?? _connectedServer;
        if (target == null)
        {
            return Result.Failure("Connection lost");
        }

        try
        {
            // Serialize Wrapper
            var wrapperJson = packet.ToJson();
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(wrapperJson);
            
            // Add Prefix 0x01 (JSON)
            var payload = new byte[jsonBytes.Length + 1];
            payload[0] = 0x01; 
            Array.Copy(jsonBytes, 0, payload, 1, jsonBytes.Length);
            
            // Chunking & Send
            // Message ID: Use Packet ID hash or random? 
            // Packet.Id is string (Guid).
            byte msgId = (byte)(packet.Id.GetHashCode() & 0xFF);
            
            var chunks = _chunker.Split(payload, msgId);

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();

                if (target.Status != Shiny.BluetoothLE.ConnectionState.Connected)
                {
                    return Result.Failure("Connection lost during transmission");
                }
                
                await target.WriteCharacteristicAsync(
                    NodusConstants.SERVICE_UUID, 
                    NodusConstants.CHARACTERISTIC_UUID, 
                    chunk, 
                    withResponse: false
                );
                
                await Task.Delay(20, ct); // Throttle
            }

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Transmission cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transmission failed");
            return Result.Failure("Transmission failed", ex);
        }
    }

    public IObservable<ConnectionState> ConnectionState => 
        _connectedServer?.WhenStatusChanged() ?? Observable.Empty<ConnectionState>();

    public bool IsConnected => _connectedServer?.Status == Shiny.BluetoothLE.ConnectionState.Connected;
    public int LastRssi { get; private set; } = -80;

    public async Task<Result> StartScanningForServerAsync(CancellationToken ct = default)
    {
        if (_bleManager.IsScanning)
        {
            _logger.LogDebug("Already scanning, skipping");
            return Result.Success();
        }
        
        // Permission Check assumed handled by caller or Shiny
        // var status = await Permissions.CheckStatusAsync<Permissions.Bluetooth>(); 
        // Note: Permissions is MAUI specific. In Shared we should rely on IBleManager acting or throw if missing.
        // Or inject IPermissionService. For now, assuming permissions are checked by the UI/App layer before calling this.

        try
        {
            CleanExpiredLinks();

            _scanSubscription = _bleManager
                .Scan(new ScanConfig(ServiceUuids: new[] { NodusConstants.SERVICE_UUID }))
                .Subscribe(
                    async result => await OnPeripheralDiscoveredAsync(result, ct),
                    ex => _logger.LogError(ex, "Scan error occurred")
                );

            _logger.LogInformation("Started scanning for Nodus servers/relays");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start scanning");
            return Result.Failure("Failed to start scanning", ex);
        }
    }

    private async Task OnPeripheralDiscoveredAsync(ScanResult result, CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            // 1. Neighbor Discovery (Firefly Trickle Logic)
            bool isRelay = result.Peripheral.Name?.Contains("Relay") == true;
            
            if (isRelay)
            {
                var key = result.Peripheral.Uuid;
                if (!_nearbyLinks.ContainsKey(key))
                {
                    _logger.LogDebug("Found Relay Neighbor: {Uuid}", key);
                }
                _nearbyLinks[key] = now.Add(_neighborTtl);
                UpdateLinkCount();
            }

            // 2. Server Discovery
            bool isServer = result.Peripheral.Name?.Contains("Nodus Server") == true;
            
            _logger.LogDebug("Found Peripheral: {Name} | RSSI: {Rssi} | Relay: {IsRelay} | Server: {IsServer}", 
                result.Peripheral.Name, result.Rssi, isRelay, isServer);

            // Update RSSI tracking
            LastRssi = result.Rssi;

            ServerDiscovered?.Invoke(this, result.Peripheral.Name ?? "Unknown");

            // Auto-Connect logic (only if not already connected)
            if (!IsConnected && (isServer || isRelay))
            {
                // Wrap IPeripheral in testable wrapper
                var wrapper = new BlePeripheralWrapper(result.Peripheral);
                _ = ConnectAsync(wrapper, DefaultTimeout, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing discovered peripheral");
        }
    }

    public void StopScanning()
    {
        _scanSubscription?.Dispose();
        _scanSubscription = null;
        _nearbyLinks.Clear();
        UpdateLinkCount();
        _logger.LogInformation("Stopped scanning");
    }
    
    private void CleanExpiredLinks()
    {
        var now = DateTime.UtcNow;
        var expired = _nearbyLinks.Where(x => x.Value < now).Select(x => x.Key).ToList();
        foreach (var key in expired) _nearbyLinks.Remove(key);
        if (expired.Any()) UpdateLinkCount();
    }

    private void UpdateLinkCount()
    {
        CleanExpiredLinks();
        LinkCountChanged?.Invoke(this, _nearbyLinks.Count);
    }

    public async Task<Result> ConnectAsync(IBlePeripheralWrapper peripheral, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        timeout ??= DefaultTimeout;

        try
        {
            await _connectionLock.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("Connection cancelled before acquiring lock");
        }

        try
        {
            if (_connectedServer != null)
            {
                _logger.LogDebug("Already connected to {Name}, skipping", _connectedServer.Name);
                return Result.Success();
            }

            _logger.LogInformation("Attempting to connect to {Name} with {Timeout}s timeout", 
                peripheral.Name, timeout.Value.TotalSeconds);

            _connectionCts?.Cancel();
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _connectionCts.CancelAfter(timeout.Value);

            _connectionCts.CancelAfter(timeout.Value);

            var connectResult = await RetryWithBackoffAsync(
                async () => await ConnectWithHandshakeAsync(peripheral, _connectionCts.Token),
                MaxRetries,
                _connectionCts.Token
            );

            if (connectResult.IsSuccess)
            {
                _connectedServer = peripheral;
                ConnectionStatusChanged?.Invoke(this, true);
                ConnectionStatusChanged?.Invoke(this, true);
                _logger.LogInformation("Successfully connected to {Name}", peripheral.Name);
            }
            else
            {
                _logger.LogWarning("Failed to connect to {Name}: {Error}", peripheral.Name, connectResult.Error);
            }

            return connectResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Connection attempt cancelled or timed out");
            return Result.Failure("Connection cancelled or timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during connection");
            return Result.Failure("Connection failed", ex);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<Result> ConnectWithHandshakeAsync(IBlePeripheralWrapper peripheral, CancellationToken ct)
    {
        try
        {
            // 1. BLE Connection
            await peripheral.ConnectAsync(new ConnectionConfig { AutoConnect = false }, ct);

            if (peripheral.Status != Shiny.BluetoothLE.ConnectionState.Connected)
            {
                return Result.Failure("Peripheral not in connected state");
            }

            // 2. Send Handshake
            var keys = await GetStoredKeysAsync();
            var handshake = new NodusPacket 
            { 
                Type = MessageType.Handshake, 
                SenderId = keys.JudgeName,
            };

            var payload = new HandshakePayload 
            { 
                Name = keys.JudgeName, 
                PublicKey = keys.PublicKey 
            };
            
            var sendResult = await SendPacketAsync(handshake, payload, ct, peripheral);
            if (sendResult.IsFailure)
            {
                 return Result.Failure($"Handshake failed: {sendResult.Error}");
            }
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure("Handshake failed", ex);
        }
    }

    public async Task<Result> SendVoteAsync(Nodus.Shared.Models.Vote vote, CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            return Result.Failure("Not connected to server");
        }

        try
        {
            var keys = await GetStoredKeysAsync();
            var packet = new NodusPacket
            {
                Type = MessageType.Vote,
                SenderId = keys.JudgeName,
            };
            
            // Wrap in retry logic
            return await RetryWithBackoffAsync(
                async () => await SendPacketAsync(packet, vote, ct),
                MaxRetries,
                ct
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending vote");
            return Result.Failure("Failed to send vote", ex);
        }
    }

    /// <summary>
    /// Sends a secure packet. Encrypts payload and signs the packet.
    /// </summary>
    private async Task<Result> SendPacketAsync(NodusPacket packet, object payloadData, CancellationToken ct = default, IBlePeripheralWrapper? overridePeripheral = null)
    {
        var target = overridePeripheral ?? _connectedServer;
        // Logic check: if we have an override, we might not be "Connected" in the service sense, but connected physically.
        if (target == null || target.Status != Shiny.BluetoothLE.ConnectionState.Connected)
        {
            return Result.Failure("Not connected");
        }

        try
        {
            // 1. Get Keys
            var keys = await GetStoredKeysAsync();
            if (string.IsNullOrEmpty(keys.SharedAesKey)) 
            {
                return Result.Failure("No encryption key found");
            }

            // 2. Prepare Payload (Serialize -> Encrypt)
            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payloadData);
            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
            var aesKey = Convert.FromBase64String(keys.SharedAesKey);
            
            packet.EncryptedPayload = CryptoHelper.Encrypt(payloadBytes, aesKey);

            // 3. Sign the Packet
            var signable = ConstructSignableBlock(packet);
            packet.Signature = CryptoHelper.SignData(signable, keys.PrivateKey);
            
            var transmitResult = await TransmitPacketAsync(packet, ct, target);
            
            if (transmitResult.IsSuccess)
            {
                _logger.LogDebug("Sent {PacketType} ({ByteCount} bytes) securely", 
                    packet.Type, packet.EncryptedPayload?.Length ?? 0);
            }

            return transmitResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending packet");
            return Result.Failure("Packet send failed", ex);
        }
    }

    /// <summary>
    /// Relays a packet exactly as received (Forwarding).
    /// Does NOT re-encrypt or re-sign.
    /// </summary>
    public async Task<Result> RelayPacketAsync(NodusPacket packet, CancellationToken ct = default)
    {
        if (!IsConnected) 
        {
            _logger.LogWarning("Cannot relay packet {PacketId}: Not connected to upstream", packet.Id);
            return Result.Failure("Not connected to upstream");
        }

        try
        {
            var result = await TransmitPacketAsync(packet, ct);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("Relayed packet {PacketId} successfully", packet.Id);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error relaying packet {PacketId}", packet.Id);
            return Result.Failure($"Relay failed for packet {packet.Id}", ex);
        }
    }

    public async Task<Result> WriteRawAsync(byte[] data, CancellationToken ct = default)
    {
        if (_connectedServer == null)
            return Result.Failure("Not connected");

        try
        {
            await _connectedServer.WriteCharacteristicAsync(
                NodusConstants.SERVICE_UUID,
                NodusConstants.CHARACTERISTIC_UUID,
                data,
                withResponse: false,
                ct
            );
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure("Write failed", ex);
        }
    }

    public async Task<Result> DisconnectAsync(CancellationToken ct = default)
    {
        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connectedServer == null)
            {
                return Result.Success();
            }

            _logger.LogInformation("Disconnecting from {Name}", _connectedServer.Name);

            _connectionCts?.Cancel();
            _connectedServer?.CancelConnection();
            
            _connectedServer = null;
            ConnectionStatusChanged?.Invoke(this, false);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
            return Result.Failure("Disconnect failed", ex);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private byte[] ConstructSignableBlock(NodusPacket p)
    {
        var idBytes = System.Text.Encoding.UTF8.GetBytes(p.Id);
        var senderBytes = System.Text.Encoding.UTF8.GetBytes(p.SenderId);
        var tsBytes = BitConverter.GetBytes(p.Timestamp);
        
        var list = new List<byte>();
        list.AddRange(idBytes);
        list.AddRange(senderBytes);
        list.AddRange(tsBytes);
        list.AddRange(p.EncryptedPayload);
        
        return list.ToArray();
    }

    private async Task<(string SharedAesKey, string PrivateKey, string PublicKey, string JudgeName)> GetStoredKeysAsync()
    {
        var aes = await _secureStorage.GetAsync(NodusConstants.KEY_SHARED_AES);
        var priv = await _secureStorage.GetAsync(NodusConstants.KEY_PRIVATE_KEY);
        var pub = await _secureStorage.GetAsync(NodusConstants.KEY_PUBLIC_KEY);
        var name = await _secureStorage.GetAsync(NodusConstants.KEY_JUDGE_NAME);
        
        return (aes ?? "", priv ?? "", pub ?? "", name ?? "Unknown");
    }

    /// <summary>
    /// Retry logic with exponential backoff and Jitter for transient BLE failures.
    /// </summary>
    private async Task<Result> RetryWithBackoffAsync(
        Func<Task<Result>> operation, 
        int maxRetries, 
        CancellationToken ct)
    {
        var delay = InitialRetryDelay;
        var random = new Random();

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await operation();
            
            if (result.IsSuccess)
            {
                if (attempt > 1)
                {
                    _logger.LogInformation("Operation succeeded after {Attempts} attempts", attempt);
                }
                return result;
            }

            if (attempt < maxRetries)
            {
                // Add Jitter: +/- 20%
                var jitter = random.NextDouble() * 0.4 + 0.8; // 0.8 to 1.2
                var actualDelay = delay * jitter;

                _logger.LogWarning("Attempt {Attempt}/{MaxRetries} failed: {Error}. Retrying in {Delay}ms...", 
                    attempt, maxRetries, result.Error, actualDelay.TotalMilliseconds);
                
                await Task.Delay(actualDelay, ct);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2); // Exponential backoff
            }
            else
            {
                _logger.LogError("Operation failed after {MaxRetries} attempts: {Error}", 
                    maxRetries, result.Error);
                return result;
            }
        }

        return Result.Failure("Max retries exceeded");
    }

    public void Dispose()
    {
        StopScanning();
        _notificationSubscription?.Dispose();
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionLock.Dispose();
        _notificationsSubject?.Dispose();
        _logger.LogInformation("BleClientService disposed");
    }
}
