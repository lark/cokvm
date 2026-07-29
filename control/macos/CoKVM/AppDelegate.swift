import Cocoa
import AVFoundation
import CoreBluetooth

class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, VideoCaptureDelegate, BluetoothClientDelegate {

    var window: NSWindow!
    var videoCapture: VideoCapture?
    var bluetoothClient: BluetoothClient?
    var keyboardManager: KeyboardManager?
    var mouseManager: MouseManager?

    private var videoView: VideoView!
    private var statusBar: StatusBarView!
    private var videoReady = false
    private var btReady = false
    private var btDiscovered = false
    private var lastCapsLockOn = false
    private var capsLockEventCount = 0
    private var videoFormatLabel: NSTextField!
    private var titleAccessoryView: NSView!

    func applicationDidFinishLaunching(_ aNotification: Notification) {
        setupWindow()
        setupServices()
        setupEventHandling()

        let mainMenu = NSMenu()
        let appMenuItem = NSMenuItem()
        appMenuItem.submenu = NSMenu()

        let quitItem = NSMenuItem(title: "Quit CoKVM", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        appMenuItem.submenu?.addItem(quitItem)
        mainMenu.addItem(appMenuItem)

        NSApp.mainMenu = mainMenu
    }

    @MainActor private func setupWindow() {
        let videoWidth: CGFloat = 1280
        let videoHeight: CGFloat = 720
        let statusBarHeight: CGFloat = 30

        window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: videoWidth, height: videoHeight + statusBarHeight),
            styleMask: [.titled, .closable, .resizable, .miniaturizable],
            backing: .buffered,
            defer: false
        )
        window.title = "CoKVM"
        window.isOpaque = true
        window.backgroundColor = NSColor.white

        let view = NSView()
        view.wantsLayer = true
        view.layer?.backgroundColor = NSColor.white.cgColor

        videoView = VideoView()
        videoView.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(videoView)

        statusBar = StatusBarView()
        statusBar.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(statusBar)

        let aspectRatioConstraint = NSLayoutConstraint(
            item: videoView as Any,
            attribute: .width,
            relatedBy: .equal,
            toItem: videoView as Any,
            attribute: .height,
            multiplier: 16.0 / 9.0,
            constant: 0
        )
        aspectRatioConstraint.priority = .defaultHigh

        NSLayoutConstraint.activate([
            videoView.topAnchor.constraint(equalTo: view.topAnchor),
            videoView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            videoView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            videoView.bottomAnchor.constraint(equalTo: statusBar.topAnchor),
            aspectRatioConstraint,

            statusBar.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            statusBar.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            statusBar.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            statusBar.heightAnchor.constraint(equalToConstant: 30)
        ])

        window.contentView = view
        window.delegate = self

        setupTitleBarAccessory()

        window.orderFrontRegardless()
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        DispatchQueue.main.async { [weak self] in
            self?.videoView.becomeFirstResponder()
        }
    }

    @MainActor private func setupTitleBarAccessory() {
        titleAccessoryView = NSView(frame: NSRect(x: 0, y: 0, width: 200, height: 28))
        titleAccessoryView.wantsLayer = true

        videoFormatLabel = NSTextField(labelWithString: "")
        videoFormatLabel.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)
        videoFormatLabel.textColor = NSColor.secondaryLabelColor
        videoFormatLabel.isEditable = false
        videoFormatLabel.isBordered = false
        videoFormatLabel.drawsBackground = false
        videoFormatLabel.alignment = .right
        videoFormatLabel.translatesAutoresizingMaskIntoConstraints = false

        titleAccessoryView.addSubview(videoFormatLabel)

        NSLayoutConstraint.activate([
            videoFormatLabel.centerYAnchor.constraint(equalTo: titleAccessoryView.centerYAnchor),
            videoFormatLabel.leadingAnchor.constraint(equalTo: titleAccessoryView.leadingAnchor),
            videoFormatLabel.trailingAnchor.constraint(equalTo: titleAccessoryView.trailingAnchor, constant: -8)
        ])

        let accessoryVC = NSTitlebarAccessoryViewController()
        accessoryVC.view = titleAccessoryView
        accessoryVC.layoutAttribute = .right
        window.addTitlebarAccessoryViewController(accessoryVC)
        window.titlebarSeparatorStyle = .none
    }

    private func updateVideoFormat(_ format: String) {
        videoFormatLabel.stringValue = format
    }

    private func setupServices() {
        videoCapture = VideoCapture(delegate: self)
        bluetoothClient = BluetoothClient(delegate: self)
        bluetoothClient?.videoReadyProvider = { [weak self] in
            guard let self = self else { return false }
            return self.statusBar.isVideoActive()
        }
        keyboardManager = KeyboardManager(delegate: self)
        mouseManager = MouseManager(delegate: self)

        bluetoothClient?.start()

        let devices = videoCapture?.availableDevices() ?? []
        if devices.count >= 1 {
            if devices.count > 1 {
                statusBar.setVideoStatus(.multiSourceActive)
            } else {
                statusBar.setVideoStatus(.singleSourceActive)
            }
            videoCapture?.start()
        } else {
            statusBar.setVideoStatus(.noDevice)
        }

        statusBar.videoButtonAction = { [weak self] in
            self?.handleVideoButtonClick()
        }

        statusBar.videoLongPressAction = { [weak self] in
            self?.showVideoSourceMenu()
        }

        statusBar.btButtonAction = { [weak self] in
            self?.bluetoothClient?.initiatePairing()
        }
    }

    private func setupEventHandling() {
        _ = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .keyUp, .flagsChanged]) { [weak self] event in
            guard let self = self, let kb = self.keyboardManager else { return event }

            if event.type == .flagsChanged {
                kb.updateModifiers(event.modifierFlags)
                let capsLockOn = event.modifierFlags.contains(.capsLock)
                capsLockEventCount += 1
                if capsLockEventCount % 2 == 0 {
                    if capsLockOn != self.lastCapsLockOn {
                        self.lastCapsLockOn = capsLockOn
                        let payload = VssPayload.ledStatus(capsLock: capsLockOn)
                        self.bluetoothClient?.sendPayload(payload)
                    }
                }
                self.updateStatusBar()
                return event
            }

            if event.isARepeat {
                return event
            }

            let keyCode = UInt8(event.keyCode)

            if event.type == .keyDown {
                kb.updateModifiers(event.modifierFlags)
                if let payload = kb.onKeyDown(keyCode, modifierFlags: event.modifierFlags) {
                    self.bluetoothClient?.sendPayload(payload)
                }
            } else {
                kb.updateModifiers(event.modifierFlags)
                if let payload = kb.onKeyUp(keyCode, modifierFlags: event.modifierFlags) {
                    self.bluetoothClient?.sendPayload(payload)
                }
            }
            self.updateStatusBar()
            return event
        }

        _ = NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak self] event in
            guard let self = self, let mm = self.mouseManager else { return event }
            if let payload = mm.onMouseWheel(event.deltaY) {
                self.bluetoothClient?.sendPayload(payload)
            }
            self.updateStatusBar()
            return event
        }

        videoView.mouseMoveHandler = { [weak self] x, y, width, height, buttons in
            guard let self = self, let mm = self.mouseManager else { return }
            if let payload = mm.onMouseMove(x, y, width, height, buttons) {
                self.bluetoothClient?.sendPayload(payload)
            }
            self.updateStatusBar()
        }

        videoView.mouseDownHandler = { [weak self] x, y, width, height, buttons in
            guard let self = self, let mm = self.mouseManager else { return }
            if let payload = mm.onMouseMove(x, y, width, height, buttons) {
                self.bluetoothClient?.sendPayload(payload)
            }
            self.updateStatusBar()
        }

        videoView.mouseUpHandler = { [weak self] x, y, width, height, buttons in
            guard let self = self, let mm = self.mouseManager else { return }
            if let payload = mm.onMouseMove(x, y, width, height, buttons) {
                self.bluetoothClient?.sendPayload(payload)
            }
            self.updateStatusBar()
        }

        videoView.comboKeyHandler = { [weak self] combo in
            guard let self = self, let bt = self.bluetoothClient else { return }
            self.sendComboKey(combo, client: bt)
        }

        videoView.flagsChangedHandler = { [weak self] flags in
            guard let self = self, let kb = self.keyboardManager else { return }
            kb.updateModifiers(flags)

            let capsLockOn = flags.contains(.capsLock)
            if capsLockOn != self.lastCapsLockOn {
                self.lastCapsLockOn = capsLockOn
                let payload = VssPayload.ledStatus(capsLock: capsLockOn)
                self.bluetoothClient?.sendPayload(payload)
            }

            self.updateStatusBar()
        }

        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in
            guard let self = self else { return }
            self.updateStatusBar()
        }
    }

    private func handleVideoButtonClick() {
        if videoReady {
            videoCapture?.stop()
            let devices = videoCapture?.availableDevices() ?? []
            if devices.count > 1 {
                statusBar.setVideoStatus(.multiSourcePaused)
            } else {
                statusBar.setVideoStatus(.singleSourcePaused)
            }
        } else {
            let devices = videoCapture?.availableDevices() ?? []
            if devices.count >= 1 {
                if devices.count > 1 {
                    showVideoSourceMenu()
                } else {
                    videoCapture?.start()
                    statusBar.setVideoStatus(.singleSourceActive)
                }
            }
        }
    }

    private func showVideoSourceMenu() {
        let devices = videoCapture?.availableDevices() ?? []
        guard !devices.isEmpty else { return }

        let menu = NSMenu(title: "Select Video Source")

        let nameCounts = Dictionary(grouping: devices, by: { $0.localizedName }).mapValues { $0.count }

        for device in devices {
            let baseName = device.localizedName
            var displayName: String

            if nameCounts[baseName, default: 1] > 1 {
                let uniqueID = device.uniqueID
                let idSuffix = uniqueID.suffix(8)
                displayName = "\(baseName) (\(idSuffix))"
            } else {
                displayName = baseName
            }

            let item = NSMenuItem(title: displayName, action: #selector(selectVideoSource(_:)), keyEquivalent: "")
            item.representedObject = device
            menu.addItem(item)
        }

        let buttonPosition = statusBar.getVideoButtonPosition()
        menu.popUp(positioning: nil, at: buttonPosition, in: window.contentView)
    }

    @objc private func selectVideoSource(_ sender: NSMenuItem) {
        guard let device = sender.representedObject as? AVCaptureDevice else { return }

        videoCapture?.stop()

        let devices = videoCapture?.availableDevices() ?? []
        if devices.count > 1 {
            statusBar.setVideoStatus(.multiSourceActive)
        } else {
            statusBar.setVideoStatus(.singleSourceActive)
        }

        videoCapture?.start(with: device)
    }


    private func sendComboKey(_ combo: String, client: BluetoothClient) {
        var modifiers: UInt8 = 0
        var keycodes: [UInt8] = []

        let parts = combo.lowercased().components(separatedBy: "+")
        for part in parts {
            switch part.trimmingCharacters(in: .whitespaces) {
            case "ctrl":
                modifiers |= 0x01
            case "shift":
                modifiers |= 0x02
            case "alt", "option":
                modifiers |= 0x04
            case "cmd", "command", "win", "gui":
                modifiers |= 0x08
            case "f1": keycodes.append(0x3A)
            case "f2": keycodes.append(0x3B)
            case "f3": keycodes.append(0x3C)
            case "f4": keycodes.append(0x3D)
            case "f5": keycodes.append(0x3E)
            case "f6": keycodes.append(0x3F)
            case "f7": keycodes.append(0x40)
            case "f8": keycodes.append(0x41)
            case "f9": keycodes.append(0x42)
            case "f10": keycodes.append(0x43)
            case "f11": keycodes.append(0x44)
            case "f12": keycodes.append(0x45)
            case "del", "delete": keycodes.append(0x4C)
            case "ins", "insert": keycodes.append(0x49)
            case "home": keycodes.append(0x4A)
            case "end": keycodes.append(0x4D)
            case "pgup", "pageup": keycodes.append(0x4B)
            case "pgdn", "pagedn", "pagedown": keycodes.append(0x4E)
            case "prtsc", "printscreen": keycodes.append(0x46)
            case "scrlk", "scrolllock": keycodes.append(0x47)
            case "pause": keycodes.append(0x48)
            case "esc", "escape": keycodes.append(0x29)
            case "tab": keycodes.append(0x2B)
            case "enter", "return": keycodes.append(0x28)
            case "space": keycodes.append(0x2C)
            case "backspace": keycodes.append(0x2A)
            case "up": keycodes.append(0x52)
            case "down": keycodes.append(0x51)
            case "left": keycodes.append(0x50)
            case "right": keycodes.append(0x4F)
            default:
                break
            }
        }

        let downPayload = VssPayload.keyboard(modifiers: modifiers, keycodes: keycodes)
        client.sendPayload(downPayload)

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.05) { [weak client] in
            let upPayload = VssPayload.keyboard(modifiers: 0, keycodes: [])
            client?.sendPayload(upPayload)
        }
    }

    private func updateStatusBar() {
        guard let kb = keyboardManager, let mm = mouseManager, let screen = window?.screen else {
            return
        }

        let scale = screen.backingScaleFactor
        let logicalSize = videoView.bounds.size
        let physicalSize = (logicalSize.width * scale, logicalSize.height * scale)

        statusBar.update(
            viewportSize: (logicalSize.width, logicalSize.height),
            physicalSize: physicalSize,
            mousePos: mm.getLastWinPos(),
            mappedPos: mm.getLastMappedPos(),
            modifiers: kb.getModifiers(),
            activeKeycodes: kb.getActiveKeycodes(),
            btReady: btReady,
            btDiscovered: btDiscovered,
            capsLockOn: lastCapsLockOn
        )
    }

    func onFrameCaptured(_ pixelBuffer: CVPixelBuffer) {
    }

    func onVideoReady(_ ready: Bool, format: String) {
        videoReady = ready
        if ready {
            DispatchQueue.main.async { [weak self] in
                self?.videoView.setSession(self?.videoCapture?.captureSession)
                self?.updateVideoFormat(format)
            }
        } else {
            DispatchQueue.main.async { [weak self] in
                self?.videoView.setSession(nil)
                self?.updateVideoFormat("")
            }
        }
        updateStatusBar()
    }

    func onDevicesChanged(_ devices: [AVCaptureDevice]) {
        if devices.isEmpty {
            statusBar.setVideoStatus(.noDevice)
        } else if !videoReady {
            if devices.count > 1 {
                statusBar.setVideoStatus(.multiSourcePaused)
            } else {
                statusBar.setVideoStatus(.singleSourcePaused)
            }
        } else {
            if devices.count > 1 {
                statusBar.setVideoStatus(.multiSourceActive)
            } else {
                statusBar.setVideoStatus(.singleSourceActive)
            }
        }
    }

    func onBluetoothReady(_ ready: Bool) {
        btReady = ready
        updateStatusBar()
    }

    func onBluetoothDeviceDiscovered(_ discovered: Bool) {
        btDiscovered = discovered
        updateStatusBar()
    }

    func windowDidResize(_ notification: Notification) {
        updateStatusBar()
    }

    func windowDidBecomeKey(_ notification: Notification) {
        let capsLockOn = NSEvent.modifierFlags.contains(.capsLock)
        lastCapsLockOn = capsLockOn
        let payload = VssPayload.ledStatus(capsLock: capsLockOn)
        bluetoothClient?.sendPayload(payload)
        updateStatusBar()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        NSApplication.shared.terminate(self)
        return true
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        videoCapture?.stop()
        bluetoothClient?.stop()
        return .terminateNow
    }
}

extension AppDelegate: KeyboardManagerDelegate, MouseManagerDelegate {
    func onKeyDown(_ scancode: UInt8) {
    }

    func onKeyUp(_ scancode: UInt8) {
    }

    func onMouseWheel(_ deltaY: CGFloat) {
    }

    func onMouseMove(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, _ buttons: UInt8) {
    }
}
