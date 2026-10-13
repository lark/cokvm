using CoKVM.Protocol;

namespace CoKVM.Services
{
    public enum BluetoothState
    {
        /// <summary>No device discovered / idle.</summary>
        Idle,
        /// <summary>Device discovered, not yet connected/ready.</summary>
        Discovered,
        /// <summary>Connected and ready to send payloads.</summary>
        Ready,
    }

    /// <summary>
    /// Delegate-style callback for Bluetooth state changes.
    /// </summary>
    public interface IBluetoothDelegate
    {
        void OnBluetoothReady(bool ready);
        void OnBluetoothDeviceDiscovered(bool discovered);
        void OnBluetoothStateChanged(BluetoothState state);
    }

    public interface IBluetoothService
    {
        bool IsReady { get; }
        bool IsDiscovered { get; }

        /// <summary>Provider used to gate sending payloads (e.g. video active).</summary>
        Func<bool>? VideoReadyProvider { get; set; }

        void Start();
        void Stop();
        void SendPayload(VssPayload payload);
        void InitiatePairing();
    }
}