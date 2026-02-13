using Shiny.BluetoothLE;

namespace Nodus.Shared.Services;

/// <summary>
/// Concrete implementation of IBlePeripheralWrapper that wraps Shiny BLE extension methods.
/// This makes BLE operations testable by converting extension methods into interface methods.
/// </summary>
public class BlePeripheralWrapper : Nodus.Shared.Abstractions.IBlePeripheralWrapper
{
    private readonly IPeripheral _peripheral;

    public BlePeripheralWrapper(IPeripheral peripheral)
    {
        _peripheral = peripheral ?? throw new ArgumentNullException(nameof(peripheral));
    }

    // Properties
    public string Name => _peripheral.Name ?? "Unknown";
    public string Uuid => _peripheral.Uuid;
    public ConnectionState Status => _peripheral.Status;
    public int Mtu => _peripheral.Mtu;

    // Wrapped extension methods - now mockable!
    public Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default)
        => _peripheral.ConnectAsync(config, ct);

    public Task WriteCharacteristicAsync(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse, CancellationToken ct = default)
        => _peripheral.WriteCharacteristicAsync(serviceUuid, characteristicUuid, data, withResponse, ct);

    public Task<int> ReadRssiAsync(CancellationToken ct = default)
        => _peripheral.ReadRssiAsync(ct);

    public IObservable<ConnectionState> WhenStatusChanged()
        => _peripheral.WhenStatusChanged();

    public IObservable<BleCharacteristicResult> NotifyCharacteristic(string serviceUuid, string characteristicUuid)
        => _peripheral.NotifyCharacteristic(serviceUuid, characteristicUuid);

    public IObservable<BleCharacteristicResult> WriteCharacteristic(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse)
        => _peripheral.WriteCharacteristic(serviceUuid, characteristicUuid, data, withResponse);

    // Direct methods
    public void CancelConnection()
        => _peripheral.CancelConnection();
}
