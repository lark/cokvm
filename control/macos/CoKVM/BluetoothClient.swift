import Foundation
import AppKit
import CoreBluetooth

protocol BluetoothClientDelegate: AnyObject {
    func onBluetoothReady(_ ready: Bool)
    func onBluetoothDeviceDiscovered(_ discovered: Bool)
}

class BluetoothClient: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {

    weak var delegate: BluetoothClientDelegate?
    var isReady = false
    var isDiscovered = false
    var videoReadyProvider: (() -> Bool)?

    private var centralManager: CBCentralManager!
    private var connectedPeripheral: CBPeripheral?
    private var vssCharacteristic: CBCharacteristic?
    private var vssInfoCharacteristic: CBCharacteristic?

    private var writeQueue = DispatchQueue(label: "com.cokvm.bluetooth.write", qos: .userInteractive)
    private let deviceName = "CoKVM-Dongle"

    private let cokvmServiceUUID = CBUUID(string: "FF00")
    private let cokvmDataUUID = CBUUID(string: "FF1D")
    private let cokvmInfoUUID = CBUUID(string: "FF2C")

    private let savedPeripheralKey = "cokvm.dongle.peripheralUUID"

    init(delegate: BluetoothClientDelegate) {
        self.delegate = delegate
        super.init()
    }

    func start() {
        centralManager = CBCentralManager(delegate: self, queue: nil)
    }

    func stop() {
        writeQueue.sync {
            self.isReady = false
            self.isDiscovered = false
        }
        delegate?.onBluetoothReady(false)
        delegate?.onBluetoothDeviceDiscovered(false)

        if let peripheral = connectedPeripheral, peripheral.state == .connected {
            centralManager.cancelPeripheralConnection(peripheral)
        }
        connectedPeripheral = nil
        vssCharacteristic = nil
        vssInfoCharacteristic = nil
    }

    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        #if DEBUG
        print("[BluetoothClient] Central manager state: \(central.state.rawValue)")
        #endif

        if central.state == .poweredOn {
            let scanOptions: [String: Any] = [
                CBCentralManagerScanOptionAllowDuplicatesKey: true
            ]

            // Prefer a direct connection to a known device (skip scanning)
            if let savedUUIDString = UserDefaults.standard.string(forKey: savedPeripheralKey),
               let savedUUID = UUID(uuidString: savedUUIDString) {
                let knownPeripherals = central.retrievePeripherals(withIdentifiers: [savedUUID])
                if let peripheral = knownPeripherals.first {
                    #if DEBUG
                    print("[BluetoothClient] Found saved peripheral, connecting directly...")
                    #endif
                    connectedPeripheral = peripheral
                    peripheral.delegate = self
                    central.connect(peripheral, options: nil)
                    // Also scan as a fallback in case the direct connection fails
                    central.scanForPeripherals(withServices: [cokvmServiceUUID], options: scanOptions)
                    return
                }
            }

            // No known device; discover by scanning
            central.scanForPeripherals(withServices: [cokvmServiceUUID], options: scanOptions)
            #if DEBUG
            print("[BluetoothClient] Scanning for CoKVM-Dongle...")
            #endif
        }
    }

    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
                        advertisementData: [String : Any], rssi RSSI: NSNumber) {
        // Ignore scan results while connecting or already connected
        if connectedPeripheral != nil {
            return
        }

        guard let name = peripheral.name else { return }

        #if DEBUG
        print("[BluetoothClient] Discovered device: \(name), RSSI: \(RSSI)")
        #endif

        if name == deviceName {
            central.stopScan()
            isDiscovered = true
            delegate?.onBluetoothDeviceDiscovered(true)

            #if DEBUG
            print("[BluetoothClient] Found CoKVM-Dongle, connecting...")
            #endif

            connectedPeripheral = peripheral
            peripheral.delegate = self
            central.connect(peripheral, options: nil)
        }
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        #if DEBUG
        print("[BluetoothClient] Connected to \(peripheral.name ?? "Unknown")")
        #endif

        // Save the peripheral identifier for direct connection on next launch
        UserDefaults.standard.set(peripheral.identifier.uuidString, forKey: savedPeripheralKey)

        // Connected; stop scanning
        if central.isScanning {
            central.stopScan()
        }

        peripheral.discoverServices([cokvmServiceUUID])
    }

    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        #if DEBUG
        print("[BluetoothClient] Failed to connect: \(error?.localizedDescription ?? "Unknown error")")
        #endif

        connectedPeripheral = nil
        central.scanForPeripherals(
            withServices: [cokvmServiceUUID],
            options: [CBCentralManagerScanOptionAllowDuplicatesKey: true]
        )
    }

    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        #if DEBUG
        print("[BluetoothClient] Disconnected: \(error?.localizedDescription ?? "Unknown error")")
        #endif

        writeQueue.sync {
            self.isReady = false
            self.vssCharacteristic = nil
            self.vssInfoCharacteristic = nil
        }
        delegate?.onBluetoothReady(false)

        connectedPeripheral = nil
        central.scanForPeripherals(
            withServices: [cokvmServiceUUID],
            options: [CBCentralManagerScanOptionAllowDuplicatesKey: true]
        )
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let error = error {
            #if DEBUG
            print("[BluetoothClient] Failed to discover services: \(error.localizedDescription)")
            #endif
            return
        }

        guard let services = peripheral.services else { return }

        #if DEBUG
        print("[BluetoothClient] Discovered \(services.count) services")
        #endif

        for service in services {
            if service.uuid == cokvmServiceUUID {
                #if DEBUG
                print("[BluetoothClient] Found CoKVM service, discovering characteristics...")
                #endif
                peripheral.discoverCharacteristics([cokvmDataUUID, cokvmInfoUUID], for: service)
            }
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        if let error = error {
            #if DEBUG
            print("[BluetoothClient] Failed to discover characteristics: \(error.localizedDescription)")
            #endif
            return
        }

        guard let characteristics = service.characteristics else { return }

        var foundData = false
        var foundInfo = false

        for characteristic in characteristics {
            if characteristic.uuid == cokvmDataUUID {
                #if DEBUG
                print("[BluetoothClient] Found VSS DATA characteristic")
                #endif
                writeQueue.sync {
                    self.vssCharacteristic = characteristic
                }
                foundData = true
            } else if characteristic.uuid == cokvmInfoUUID {
                #if DEBUG
                print("[BluetoothClient] Found VSS INFO characteristic")
                #endif
                writeQueue.sync {
                    self.vssInfoCharacteristic = characteristic
                }
                foundInfo = true
            }
        }

        if foundData && foundInfo {
            #if DEBUG
            print("[BluetoothClient] Both characteristics found, reading INFO to trigger pairing/auth...")
            #endif
            if let infoChar = vssInfoCharacteristic {
                peripheral.readValue(for: infoChar)
            }
        } else if foundData && !foundInfo {
            #if DEBUG
            print("[BluetoothClient] DATA found but INFO missing, proceeding with data only")
            #endif
            writeQueue.sync {
                self.isReady = true
            }
            delegate?.onBluetoothReady(true)
        }
    }

    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        if characteristic.uuid == cokvmInfoUUID {
            if error == nil {
                #if DEBUG
                print("[BluetoothClient] INFO read success, device authenticated and ready")
                #endif
                writeQueue.sync {
                    self.isReady = true
                }
                delegate?.onBluetoothReady(true)
            } else {
                #if DEBUG
                print("[BluetoothClient] INFO read failed: \(error!.localizedDescription)")
                #endif
                writeQueue.sync {
                    self.isReady = false
                }
                delegate?.onBluetoothReady(false)
            }
        }
    }

    func sendPayload(_ payload: VssPayload) {
        writeQueue.async { [weak self] in
            guard let self = self else { return }

            let videoReady = self.videoReadyProvider?() ?? false
            guard self.isReady && videoReady,
                  let peripheral = self.connectedPeripheral,
                  let characteristic = self.vssCharacteristic else {
                return
            }

            let data = payload.payload
            peripheral.writeValue(data, for: characteristic, type: .withoutResponse)
        }
    }

    func initiatePairing() {
        guard let peripheral = connectedPeripheral else {
            #if DEBUG
            print("[BluetoothClient] initiatePairing: not connected, ensuring scan is active")
            #endif
            if centralManager.state == .poweredOn && !centralManager.isScanning {
                centralManager.scanForPeripherals(
                    withServices: [cokvmServiceUUID],
                    options: [CBCentralManagerScanOptionAllowDuplicatesKey: true]
                )
            }
            return
        }

        guard let infoChar = vssInfoCharacteristic else {
            #if DEBUG
            print("[BluetoothClient] initiatePairing: INFO characteristic not found")
            #endif
            return
        }

        #if DEBUG
        print("[BluetoothClient] initiatePairing: reading INFO to trigger pairing")
        #endif
        peripheral.readValue(for: infoChar)
    }
}