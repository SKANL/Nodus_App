using Shiny.BluetoothLE;

namespace Nodus.Shared.Abstractions;

/// <summary>
/// Wrapper interface for IPeripheral that makes Shiny BLE extension methods mockable.
/// This enables comprehensive unit testing of BLE operations.
/// </summary>
public interface IBlePeripheralWrapper
{
    // Properties from IPeripheral
    string Name { get; }
    string Uuid { get; }
    ConnectionState Status { get; }
    int Mtu { get; }

    // Wrapped extension methods (now mockable)
    Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default);
    Task WriteCharacteristicAsync(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse, CancellationToken ct = default);
    Task<int> ReadRssiAsync(CancellationToken ct = default);
    IObservable<ConnectionState> WhenStatusChanged();
    IObservable<BleCharacteristicResult> NotifyCharacteristic(string serviceUuid, string characteristicUuid);
    IObservable<BleCharacteristicResult> WriteCharacteristic(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse);
    
    // Direct IPeripheral methods
    void CancelConnection();
}
