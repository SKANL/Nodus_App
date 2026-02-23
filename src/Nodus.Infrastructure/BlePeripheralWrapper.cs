using Shiny.BluetoothLE;
using System.Reactive.Linq;

namespace Nodus.Infrastructure.Services;

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
    public string ConnectionState => _peripheral.Status.ToString();
    public int Mtu => _peripheral.Mtu;

    // Wrapped extension methods - now mockable!
    public Task ConnectAsync(object config, CancellationToken ct = default)
        => _peripheral.ConnectAsync(config as ConnectionConfig, ct);

    public Task WriteCharacteristicAsync(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse, CancellationToken ct = default)
        => _peripheral.WriteCharacteristicAsync(serviceUuid, characteristicUuid, data, withResponse, ct);

    public Task<int> ReadRssiAsync(CancellationToken ct = default)
        => _peripheral.ReadRssiAsync(ct);

    public IObservable<string> WhenStatusChanged()
        => _peripheral.WhenStatusChanged().Select(s => s.ToString());

    public IObservable<(string Characteristic, byte[] Data)> NotifyCharacteristic(string serviceUuid, string characteristicUuid)
        => _peripheral.NotifyCharacteristic(serviceUuid, characteristicUuid)
           .Select(x => (x.Characteristic.Uuid, x.Data ?? Array.Empty<byte>()));

    public IObservable<(string Characteristic, byte[] Data)> WriteCharacteristic(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse)
        => _peripheral.WriteCharacteristic(serviceUuid, characteristicUuid, data, withResponse)
           .Select(x => (x.Characteristic.Uuid, x.Data ?? Array.Empty<byte>()));

    // Direct methods
    public void CancelConnection()
        => _peripheral.CancelConnection();

    public async Task<Nodus.Shared.Models.BleResult> ReadCharacteristicAsync(string serviceUuid, string characteristicUuid, CancellationToken ct = default)
    {
        try
        {
            var result = await _peripheral.ReadCharacteristicAsync(serviceUuid, characteristicUuid, ct);
            return new Nodus.Shared.Models.BleResult
            {
                Data = result.Data,
                CharacteristicUuid = characteristicUuid,
                ServiceUuid = serviceUuid,
                Success = true
            };
        }
        catch (Exception ex)
        {
            return new Nodus.Shared.Models.BleResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                CharacteristicUuid = characteristicUuid,
                ServiceUuid = serviceUuid
            };
        }
    }
}
