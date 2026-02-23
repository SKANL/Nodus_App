using System;
using System.Threading;
using System.Threading.Tasks;
using Nodus.Shared.Models;

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
    string ConnectionState { get; }
    int Mtu { get; }

    // Wrapped extension methods (now mockable)
    Task ConnectAsync(object config, CancellationToken ct = default);
    Task WriteCharacteristicAsync(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse, CancellationToken ct = default);
    Task<int> ReadRssiAsync(CancellationToken ct = default);
    IObservable<string> WhenStatusChanged();
    IObservable<(string Characteristic, byte[] Data)> NotifyCharacteristic(string serviceUuid, string characteristicUuid);
    IObservable<(string Characteristic, byte[] Data)> WriteCharacteristic(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse);
    Task<BleResult> ReadCharacteristicAsync(string serviceUuid, string characteristicUuid, CancellationToken ct = default);
    
    // Direct IPeripheral methods
    void CancelConnection();
}
