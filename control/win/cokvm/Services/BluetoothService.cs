using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using CoKVM.Protocol;

namespace CoKVM.Services
{
    /// <summary>
    /// BLE client for the CoKVM-Dongle using the custom VSS service.
    ///
    /// Service:        FF00
    /// Data characteristic (write):  FF1D
    /// Info characteristic (read):   FF2C  (read to trigger pairing/auth)
    ///
    /// Discovery uses BluetoothLEAdvertisementWatcher (more reliable for advertising
    /// BLE devices than DeviceWatcher by name).
    /// Reference: control/macos/CoKVM/BluetoothClient.swift
    /// </summary>
    public class BluetoothService : IBluetoothService
    {
        private static readonly Guid ServiceUuid =
            Guid.Parse("0000ff00-0000-1000-8000-00805f9b34fb");
        private static readonly Guid DataCharacteristicUuid =
            Guid.Parse("0000ff1d-0000-1000-8000-00805f9b34fb");
        private static readonly Guid InfoCharacteristicUuid =
            Guid.Parse("0000ff2c-0000-1000-8000-00805f9b34fb");

        private const string DeviceName = "CoKVM-Dongle";
        private const string SavedDeviceIdKey = "cokvm.dongle.deviceId";

        private readonly IBluetoothDelegate _delegate;
        private BluetoothLEAdvertisementWatcher? _watcher;
        private BluetoothLEDevice? _device;
        private GattCharacteristic? _dataCharacteristic;
        private GattCharacteristic? _infoCharacteristic;
        private bool _connecting;
        // Cached saved (bonded) device id. FromIdAsync on this id reconnects using the
        // bond -> the link encrypts fast -> authen characteristics (FF1D/FF2C) become
        // visible. FromBluetoothAddressAsync (raw address) connects unencrypted and the
        // authen characteristics stay hidden, so it's only the fallback for first-ever
        // pairing when no saved id exists yet.
        private string? _savedDeviceId;
        // Reconnect cooldown (Environment.TickCount64). Right after a Windows-initiated
        // disconnect (reason 0x13) the stack is still tearing down the previous
        // connection's security context; reconnecting immediately connects at the GATT
        // level but L4 encryption doesn't complete, so authen characteristics stay
        // hidden and the attempt fails. Skip connect attempts until this timestamp.
        private long _noConnectUntilTicks;

        private readonly object _lock = new();
        private volatile bool _isReady;
        private volatile bool _isDiscovered;

        public bool IsReady => _isReady;
        public bool IsDiscovered => _isDiscovered;
        public Func<bool>? VideoReadyProvider { get; set; }

        public BluetoothService(IBluetoothDelegate @delegate)
        {
            _delegate = @delegate;
        }

        public void Start()
        {
            Log("Start()");
            _savedDeviceId = ReadSavedDeviceId();
            // Scan immediately AND try the saved bonded device in parallel — whichever
            // connects first wins. (Previously scanning waited for the saved-id attempt
            // to finish, which delayed detection by up to ~10s when that attempt was
            // slow or the dongle wasn't present at launch.) Mirrors macOS, which scans
            // as a fallback alongside a direct saved-peripheral connect.
            StartScanning();
            _ = TryConnectSavedDeviceAsync();
        }

        private async Task TryConnectSavedDeviceAsync()
        {
            try
            {
                string? savedId = _savedDeviceId;
                if (string.IsNullOrEmpty(savedId)) return;
                Log("Saved device id found, trying direct connect: " + savedId);
                await ConnectFromAsync(() => BluetoothLEDevice.FromIdAsync(savedId).AsTask(), "saved");
            }
            catch (Exception ex)
            {
                Log("Saved connect failed: " + ex.Message);
            }
        }

        private void StartScanning()
        {
            Log("StartScanning()");
            // Stop any existing watcher before starting a new one — otherwise a
            // disconnect-triggered rescan would leave two watchers running and double
            // every event.
            try { _watcher?.Stop(); } catch { }
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            _watcher.Received += OnAdvertisementReceived;
            try
            {
                _watcher.Start();
                Log("Advertisement watcher started");
            }
            catch (Exception ex)
            {
                Log("Watcher start failed: " + ex.Message);
            }
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            string name = args.Advertisement.LocalName ?? "";
            bool hasService = false;
            foreach (var uuid in args.Advertisement.ServiceUuids)
            {
                if (uuid == ServiceUuid) { hasService = true; break; }
            }

            // Match by the device name "CoKVM-Dongle" OR by the CoKVM service UUID in
            // the advertisement. The three-UUID GATT check in ConnectAsync is the real
            // identity gate; this just decides which advertisements are worth a connect
            // attempt.
            bool match = name.Equals(DeviceName, StringComparison.OrdinalIgnoreCase) || hasService;
            if (!match) return;

            // An advertisement means the dongle is actually present and reachable —
            // that's what "discovered" (yellow) should mean. Don't set it on speculative
            // saved-id reconnect attempts (e.g. right after an unplug), which would show
            // yellow misleadingly when the dongle is gone.
            if (!_isDiscovered) SetDiscovered(true);

            // Prefer the saved (bonded) device id: FromIdAsync reconnects using the
            // bond, so the link encrypts quickly and the authenticated characteristics
            // (FF1D/FF2C) are discoverable. FromBluetoothAddressAsync connects by raw
            // address without the bond -> unencrypted -> characteristics hidden -> the
            // identity check fails and we'd loop slowly until the link eventually
            // encrypts (this was the ~27s reconnect delay). Fall back to the address
            // path only when there's no saved id yet (first-ever pairing).
            string? savedId = _savedDeviceId;
            if (!string.IsNullOrEmpty(savedId))
            {
                _ = ConnectFromAsync(() => BluetoothLEDevice.FromIdAsync(savedId).AsTask(), "adv-saved");
            }
            else
            {
                ulong addr = args.BluetoothAddress;
                _ = ConnectFromAsync(() => BluetoothLEDevice.FromBluetoothAddressAsync(addr).AsTask(), $"addr:{addr:X12}");
            }
        }

        /// <summary>
        /// Single guarded entry for all connection attempts (saved-id direct connect and
        /// advertisement-driven connect). Atomically claims the connect slot so the two
        /// parallel paths can't both connect at once; the loser returns immediately.
        /// Holds _connecting for the duration of ConnectAsync — on failure ConnectAsync
        /// calls ClearConnection, releasing the slot so the next advertisement retries.
        /// </summary>
        private async Task ConnectFromAsync(Func<Task<BluetoothLEDevice>> obtain, string label)
        {
            lock (_lock)
            {
                if (_connecting || _device != null) return;
                // Honor the post-disconnect cooldown (see _noConnectUntilTicks).
                if (Environment.TickCount64 < _noConnectUntilTicks) return;
                _connecting = true;
            }
            Log("ConnectFrom " + label + " (proceeding)");

            try
            {
                // The first connection right after dongle power-on often fails GATT
                // discovery (Windows' security/cache not yet primed), but a fresh
                // reconnect finds the characteristics immediately. Retry once more with a
                // short delay; if that also fails, stop churning and let the next
                // advertisement drive another attempt (rapid retries just churn the link
                // and don't speed up Windows' recovery).
                for (int tryNum = 1; tryNum <= 2; tryNum++)
                {
                    if (_device != null) break; // another path connected
                    BluetoothLEDevice? device;
                    try { device = await obtain(); }
                    catch (Exception ex) { Log(label + " obtain failed: " + ex.Message); break; }
                    if (device == null) { Log(label + ": no device"); break; }
                    Log(label + ": got device name=" + (device.Name ?? "?") + " (try " + tryNum + ")");
                    bool ready = await ConnectAsync(device);
                    if (ready || _device != null) break;

                    if (tryNum < 2)
                    {
                        Log($"Connect try {tryNum} did not reach ready; retrying");
                        await Task.Delay(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(label + " failed: " + ex.Message);
            }
            finally
            {
                lock (_lock) _connecting = false;
            }
        }

        private async Task<bool> ConnectAsync(BluetoothLEDevice device)
        {
            bool success = false;
            lock (_lock)
            {
                _device?.Dispose();
                _device = device;
            }

            device.ConnectionStatusChanged += OnConnectionStatusChanged;

            try
            {
                // Wait briefly for the link to come up before GATT discovery. Kept short
                // so a failed/absent device bails fast and frees the connect slot for the
                // next advertisement (the dongle re-advertises ~1/s).
                await WaitForConnectedAsync(device, TimeSpan.FromSeconds(2));

                // The FF00 service is discoverable unencrypted, but its FF1D/FF2C
                // characteristics are authenticated and only appear once the link is
                // encrypted (FromIdAsync on a bonded id encrypts fast). Use Uncached so
                // each attempt does a fresh ATT discovery instead of returning a stale
                // "no characteristics" cached result from an earlier unencrypted attempt.
                var servicesResult = await device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached);
                Log("GetGattServices status=" + servicesResult.Status + " count=" + servicesResult.Services.Count);
                if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                {
                    // Retry once after a short delay (the link can flap during security escalation).
                    await Task.Delay(500);
                    servicesResult = await device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached);
                    Log("Retry GetGattServices status=" + servicesResult.Status + " count=" + servicesResult.Services.Count);
                    if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                    {
                        SetReady(false);
                        return false;
                    }
                }

                // Discover ALL characteristics of the FF00 service and match by UUID
                // client-side. Windows' GetCharacteristicsForUuidAsync can return empty
                // for authenticated characteristics even when the link is encrypted (an
                // ATT/cache quirk); a full discovery + client-side UUID match is more
                // reliable and mirrors macOS (discoverCharacteristics then match by uuid).
                // Authen characteristics only appear once the link encrypts, so retry on
                // the SAME connection instead of tearing down (which caused the
                // "connect, disconnect once, reconnect" cycle).
                GattCharacteristic? dataChar = null;
                GattCharacteristic? infoChar = null;
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    dataChar = null; infoChar = null;
                    foreach (var service in servicesResult.Services)
                    {
                        var charsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                        if (charsResult.Status != GattCommunicationStatus.Success) continue;
                        foreach (var ch in charsResult.Characteristics)
                        {
                            if (ch.Uuid == DataCharacteristicUuid &&
                                (ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                                 ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)))
                            {
                                dataChar = ch;
                            }
                            else if (ch.Uuid == InfoCharacteristicUuid)
                            {
                                infoChar = ch;
                            }
                        }
                    }

                    // DATA (FF1D) is required; INFO (FF2C) is optional (proceed data-only
                    // like macOS if absent). Retry only while DATA is missing.
                    if (dataChar != null) break;

                    if (attempt < 5)
                    {
                        Log($"Characteristics not yet visible (attempt {attempt}, data={dataChar != null} info={infoChar != null}); waiting for encryption");
                        await Task.Delay(400);
                    }
                }

                if (dataChar == null)
                {
                    Log("DATA characteristic FF1D not found after retries — not our dongle");
                    SetReady(false);
                    return false;
                }

                lock (_lock) _dataCharacteristic = dataChar;
                Log("Found DATA characteristic FF1D props=" + dataChar.CharacteristicProperties);
                if (infoChar != null)
                {
                    lock (_lock) _infoCharacteristic = infoChar;
                    Log("Found INFO characteristic FF2C");
                }

                SaveDeviceId(device.DeviceId);

                // Read INFO (if present) to trigger/verify encryption. On an unpaired
                // dongle the read fails -> status stays "yellow" (Discovered); the user
                // clicks the Bluetooth button (InitiatePairing) to complete pairing. If
                // INFO is absent, proceed data-only (macOS does the same).
                if (infoChar != null)
                {
                    Log("IsPaired before read: " + device.DeviceInformation.Pairing.IsPaired);
                    bool ok = false;
                    for (int attempt = 1; attempt <= 5; attempt++)
                    {
                        try
                        {
                            var readResult = await infoChar.ReadValueAsync();
                            Log("INFO read attempt " + attempt + " status=" + readResult.Status
                                + " IsPaired=" + device.DeviceInformation.Pairing.IsPaired);
                            if (readResult.Status == GattCommunicationStatus.Success)
                            {
                                ok = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("INFO read attempt " + attempt + " exception: " + ex.Message);
                        }
                        await Task.Delay(1500);
                    }
                    SetReady(ok);
                    success = ok;
                }
                else
                {
                    Log("INFO FF2C not found; proceeding data-only");
                    SetReady(true);
                    success = true;
                }
            }
            finally
            {
                if (!success)
                {
                    // The connect/discover/auth sequence didn't fully succeed. Tear down
                    // so the next advertisement retries from a clean state — otherwise
                    // _device stays set and OnAdvertisementReceived's guard blocks all
                    // reconnection (this was why an already-paired dongle hot-plugged in
                    // never reconnected, and why the BT button couldn't recover it).
                    Log("ConnectAsync did not reach ready; clearing connection for retry");
                    ClearConnection();
                }
            }
            return success;
        }

        /// <summary>
        /// Tear down the current connection state (device + characteristics) so scanning
        /// can rediscover and reconnect. Does NOT notify the delegate — callers do that
        /// via SetReady(false) where appropriate.
        /// </summary>
        private void ClearConnection()
        {
            BluetoothLEDevice? dev;
            lock (_lock)
            {
                _isReady = false;
                _dataCharacteristic = null;
                _infoCharacteristic = null;
                dev = _device;
                _device = null;
                _loggedFirstWriteUuid = false;
                _failStreak = 0;
            }
            if (dev != null)
            {
                try { dev.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { }
                try { dev.Dispose(); } catch { }
            }
        }

        private async Task WaitForConnectedAsync(BluetoothLEDevice device, TimeSpan timeout)
        {
            if (device.ConnectionStatus == BluetoothConnectionStatus.Connected) return;
            var tcs = new TaskCompletionSource<bool>();
            TypedEventHandler<BluetoothLEDevice, object> handler = (s, a) =>
            {
                if (s.ConnectionStatus == BluetoothConnectionStatus.Connected)
                    tcs.TrySetResult(true);
            };
            device.ConnectionStatusChanged += handler;
            try
            {
                await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            }
            finally
            {
                device.ConnectionStatusChanged -= handler;
            }
        }

        private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            Log("ConnectionStatus=" + sender.ConnectionStatus);
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                ClearConnection();
                // Show "no device" (gray/Idle) like macOS — the dongle is gone (or the
                // link dropped) until scanning finds it again. Previously stayed
                // "discovered" (yellow) because _isDiscovered was never cleared.
                SetDiscovered(false);
                _delegate.OnBluetoothReady(false);
                // Cooldown: don't reconnect immediately — Windows needs a moment to
                // release the previous connection's security context, and reconnecting
                // too soon fails GATT discovery. The advertisement watcher (restarted
                // below) drives reconnection once the dongle advertises again and the
                // cooldown elapses. (Mirrors macOS, which resumes scanning on disconnect
                // rather than immediately retrying the saved peripheral.)
                _noConnectUntilTicks = Environment.TickCount64 + 2000;
                StartScanning();
            }
        }

        public void Stop()
        {
            Log("Stop()");
            lock (_lock)
            {
                _isReady = false;
                _isDiscovered = false;
                _dataCharacteristic = null;
                _infoCharacteristic = null;
            }

            try { _watcher?.Stop(); } catch { }
            _watcher = null;

            try
            {
                if (_device != null)
                {
                    _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                    _device.Dispose();
                    _device = null;
                }
            }
            catch { }

            _delegate.OnBluetoothReady(false);
            _delegate.OnBluetoothDeviceDiscovered(false);
            NotifyState();
        }

        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private int _sendCount;
        private long _lastSkipLogTicks;
        private int _failStreak;            // consecutive write failures (throttles failure logs)
        private bool _loggedFirstWriteUuid; // logs the target UUID once per connection

        public async void SendPayload(VssPayload payload)
        {
            string? skip = null;
            if (!_isReady) skip = "not ready";
            else if (!(VideoReadyProvider?.Invoke() ?? false)) skip = "video not active";

            GattCharacteristic? dataChar = null;
            if (skip == null)
            {
                lock (_lock) { dataChar = _dataCharacteristic; }
                if (dataChar == null) skip = "no data char";
            }

            if (skip != null)
            {
                // Throttle skip logging to once per second to avoid flooding on mouse move.
                long now = Environment.TickCount64;
                if (now - _lastSkipLogTicks > 1000)
                {
                    _lastSkipLogTicks = now;
                    Log("SendPayload skipped: " + skip);
                }
                return;
            }

            // Serialize writes: overlapping WriteValueAsync calls on the same GATT
            // characteristic fail silently. This matches the macOS serial writeQueue.
            await _writeGate.WaitAsync();
            try
            {
                // Re-read under the gate; a disconnect may have cleared it while we waited.
                GattCharacteristic? ch;
                lock (_lock) { ch = _dataCharacteristic; }
                if (ch == null) return;

                byte[] bytes = payload.ToPayload();
                var writer = new Windows.Storage.Streams.DataWriter();
                writer.WriteBytes(bytes);
                var buffer = writer.DetachBuffer();

                var writeOption = ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                    ? GattWriteOption.WriteWithoutResponse
                    : GattWriteOption.WriteWithResponse;

                if (!_loggedFirstWriteUuid)
                {
                    _loggedFirstWriteUuid = true;
                    Log($"First write -> {ch.Uuid} props={ch.CharacteristicProperties} option={writeOption}");
                }

                // CHECK the status — previously it was ignored, so failed writes (auth,
                // wrong option, concurrency) were logged as successes.
                var status = await ch.WriteValueAsync(buffer, writeOption);
                // Keyboard (0x01) and LED (0x03) payloads are infrequent — log every
                // one (success and failure) so bt.log confirms they reach the write path
                // and shows their status. Mouse (0x02) is high-frequency, so throttle.
                bool isMouse = payload.Type == 0x02;
                if (status == GattCommunicationStatus.Success)
                {
                    _sendCount++;
                    _failStreak = 0;
                    if (!isMouse)
                    {
                        Log($"Write ok type=0x{payload.Type:X2} (#{_sendCount})");
                    }
                    else if (_sendCount % 200 == 0)
                    {
                        Log($"SendPayload: {_sendCount} writes so far");
                    }
                }
                else
                {
                    _failStreak++;
                    if (!isMouse || _failStreak == 1 || _failStreak % 50 == 0)
                    {
                        Log($"WriteValue status={status} type=0x{payload.Type:X2} option={writeOption} (streak={_failStreak})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("WriteValue exception: " + ex.Message);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async void InitiatePairing()
        {
            Log("InitiatePairing()");
            GattCharacteristic? infoChar;
            BluetoothLEDevice? device;
            lock (_lock) { infoChar = _infoCharacteristic; device = _device; }

            if (device != null && infoChar != null)
            {
                // Re-read the authenticated INFO characteristic to (re)trigger
                // pairing/auth. Retry a few times so a single button click sees the
                // pairing through to a ready state (the OS pairing dialog is driven by
                // the user completing it).
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        var readResult = await infoChar.ReadValueAsync();
                        Log("InitiatePairing INFO read attempt " + attempt + " status=" + readResult.Status
                            + " IsPaired=" + device.DeviceInformation.Pairing.IsPaired);
                        if (readResult.Status == GattCommunicationStatus.Success)
                        {
                            SetReady(true);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("InitiatePairing INFO read attempt " + attempt + " exception: " + ex.Message);
                    }
                    await Task.Delay(1500);
                }
                SetReady(false);
                // The GATT session may be stale after pairing completed (characteristics
                // captured before encryption can become unusable). Tear down and rescan so
                // the next advertisement reconnects on a clean, encrypted link and
                // ConnectAsync re-reads INFO successfully -> ready (green).
                ClearConnection();
                StartScanning();
            }
            else if (_watcher == null)
            {
                StartScanning();
            }
        }

        private void SetDiscovered(bool discovered)
        {
            _isDiscovered = discovered;
            _delegate.OnBluetoothDeviceDiscovered(discovered);
            NotifyState();
        }

        private void SetReady(bool ready)
        {
            _isReady = ready;
            Log("SetReady(" + ready + ")");
            _delegate.OnBluetoothReady(ready);
            NotifyState();
        }

        private void NotifyState()
        {
            BluetoothState state = _isReady ? BluetoothState.Ready
                              : _isDiscovered ? BluetoothState.Discovered
                              : BluetoothState.Idle;
            _delegate.OnBluetoothStateChanged(state);
        }

        private static string LogDir => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoKVM");

        private static void Log(string message)
        {
            try
            {
                System.IO.Directory.CreateDirectory(LogDir);
                string path = System.IO.Path.Combine(LogDir, "bt.log");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        // Unpackaged apps have no ApplicationData.Current; persist the bonded device id
        // to a file for auto-reconnect.
        private static string SavedDeviceIdPath => System.IO.Path.Combine(LogDir, "device.txt");

        private static string? ReadSavedDeviceId()
        {
            try
            {
                if (System.IO.File.Exists(SavedDeviceIdPath))
                    return System.IO.File.ReadAllText(SavedDeviceIdPath).Trim();
            }
            catch { }
            return null;
        }

        private void SaveDeviceId(string id)
        {
            _savedDeviceId = id;
            try
            {
                System.IO.Directory.CreateDirectory(LogDir);
                System.IO.File.WriteAllText(SavedDeviceIdPath, id);
            }
            catch { }
        }
    }
}