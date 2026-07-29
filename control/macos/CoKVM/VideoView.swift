import Cocoa
import AVFoundation

class VideoView: NSView {

    var mouseMoveHandler: ((CGFloat, CGFloat, CGFloat, CGFloat, UInt8) -> Void)?
    var mouseDownHandler: ((CGFloat, CGFloat, CGFloat, CGFloat, UInt8) -> Void)?
    var mouseUpHandler: ((CGFloat, CGFloat, CGFloat, CGFloat, UInt8) -> Void)?
    var flagsChangedHandler: ((NSEvent.ModifierFlags) -> Void)?
    var comboKeyHandler: ((String) -> Void)?

    private var previewLayer: AVCaptureVideoPreviewLayer?
    private var comboPanel: ComboKeyPanel?

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        setupView()
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        setupView()
    }

    private func setupView() {
        wantsLayer = true
        layer?.backgroundColor = NSColor.black.cgColor
        setupTrackingArea()
    }

    private func setupTrackingArea() {
        let options: NSTrackingArea.Options = [
            .mouseMoved,
            .mouseEnteredAndExited,
            .activeInKeyWindow
        ]
        let trackingArea = NSTrackingArea(
            rect: bounds,
            options: options,
            owner: self,
            userInfo: nil
        )
        addTrackingArea(trackingArea)
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        for area in trackingAreas {
            removeTrackingArea(area)
        }
        setupTrackingArea()
    }

    override func layout() {
        super.layout()
        previewLayer?.frame = bounds
    }

    func setSession(_ session: AVCaptureSession?) {
        if let previewLayer = previewLayer {
            previewLayer.removeFromSuperlayer()
        }

        guard let session = session else {
            return
        }

        previewLayer = AVCaptureVideoPreviewLayer(session: session)
        previewLayer?.frame = bounds
        previewLayer?.videoGravity = .resizeAspect
        if #available(macOS 14.0, *) {
            let portraitAngle = CGFloat(Double.pi / 2.0)
            if previewLayer?.connection?.isVideoRotationAngleSupported(portraitAngle) ?? false {
                previewLayer?.connection?.videoRotationAngle = portraitAngle
            }
        }

        if let layer = previewLayer {
            self.layer?.addSublayer(layer)
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
    }

    override func mouseEntered(with event: NSEvent) {
        window?.makeFirstResponder(self)
    }

    private var isComboPanelVisible: Bool {
        comboPanel != nil && comboPanel!.isVisible
    }

    override func mouseMoved(with event: NSEvent) {
        if isComboPanelVisible { return }
        let point = convert(event.locationInWindow, from: nil)
        let buttons = getMouseButtons()
        mouseMoveHandler?(point.x, point.y, bounds.width, bounds.height, buttons)
    }

    override func mouseDragged(with event: NSEvent) {
        if isComboPanelVisible { return }
        let point = convert(event.locationInWindow, from: nil)
        let buttons = getMouseButtons()
        mouseMoveHandler?(point.x, point.y, bounds.width, bounds.height, buttons)
    }

    override func mouseDown(with event: NSEvent) {
        if isComboPanelVisible { return }
        let point = convert(event.locationInWindow, from: nil)
        let buttons = getMouseButtons()
        mouseDownHandler?(point.x, point.y, bounds.width, bounds.height, buttons)
    }

    override func mouseUp(with event: NSEvent) {
        if isComboPanelVisible { return }
        let point = convert(event.locationInWindow, from: nil)
        let buttons = getMouseButtons()
        mouseUpHandler?(point.x, point.y, bounds.width, bounds.height, buttons)
    }

    override func rightMouseDown(with event: NSEvent) {
        if event.modifierFlags.contains(.control) {
            toggleComboPanel(at: NSEvent.mouseLocation)
            return
        }
        if isComboPanelVisible {
            closeComboPanel()
            return
        }
        let point = convert(event.locationInWindow, from: nil)
        let buttons = getMouseButtons()
        mouseDownHandler?(point.x, point.y, bounds.width, bounds.height, buttons)
    }

    override func rightMouseUp(with event: NSEvent) {
        if event.modifierFlags.contains(.control) {
            return
        }
        if isComboPanelVisible {
            return
        }
        let point = convert(event.locationInWindow, from: nil)
        let buttons = getMouseButtons()
        mouseUpHandler?(point.x, point.y, bounds.width, bounds.height, buttons)
    }

    // MARK: - Combo Key Panel

    private func toggleComboPanel(at screenPoint: NSPoint) {
        if let panel = comboPanel, panel.isVisible {
            closeComboPanel()
        } else {
            showComboPanel(at: screenPoint)
        }
    }

    private func showComboPanel(at screenPoint: NSPoint) {
        let panel = ComboKeyPanel()
        panel.comboKeyHandler = { [weak self] combo in
            self?.comboKeyHandler?(combo)
        }
        panel.onClose = { [weak self] in
            if let panel = self?.comboPanel {
                self?.window?.removeChildWindow(panel)
            }
            self?.comboPanel = nil
        }
        panel.setFrameOrigin(screenPoint)
        window?.addChildWindow(panel, ordered: .above)
        comboPanel = panel
    }

    private func closeComboPanel() {
        if let panel = comboPanel {
            window?.removeChildWindow(panel)
            panel.close()
        }
        comboPanel = nil
    }

    private func getMouseButtons() -> UInt8 {
        var buttons: UInt8 = 0
        let pressed = NSEvent.pressedMouseButtons

        if pressed & 0x1 != 0 {
            buttons |= 0x01
        }
        if pressed & 0x2 != 0 {
            buttons |= 0x02
        }
        if pressed & 0x4 != 0 {
            buttons |= 0x04
        }

        return buttons
    }

    override var acceptsFirstResponder: Bool {
        return true
    }

    override func flagsChanged(with event: NSEvent) {
        flagsChangedHandler?(event.modifierFlags)
    }

    override func keyDown(with event: NSEvent) {
        super.keyDown(with: event)
    }
}

// MARK: - ComboKeyPanel

private class ComboKeyPanel: NSPanel {

    var comboKeyHandler: ((String) -> Void)?
    var onClose: (() -> Void)?

    private var keyCombos: [String] = []

    private struct KeyDef {
        let title: String
        let combo: String
    }

    private let keyLayout: [[KeyDef]] = [
        [KeyDef(title: "F1", combo: "f1"), KeyDef(title: "F2", combo: "f2"),
         KeyDef(title: "F3", combo: "f3"), KeyDef(title: "F4", combo: "f4"),
         KeyDef(title: "F5", combo: "f5"), KeyDef(title: "F6", combo: "f6")],
        [KeyDef(title: "F7", combo: "f7"), KeyDef(title: "F8", combo: "f8"),
         KeyDef(title: "F9", combo: "f9"), KeyDef(title: "F10", combo: "f10"),
         KeyDef(title: "F11", combo: "f11"), KeyDef(title: "F12", combo: "f12")],
        [KeyDef(title: "Esc", combo: "esc"), KeyDef(title: "Tab", combo: "tab"),
         KeyDef(title: "Del", combo: "del"), KeyDef(title: "Ins", combo: "ins"),
         KeyDef(title: "Home", combo: "home"), KeyDef(title: "End", combo: "end")],
        [KeyDef(title: "PgUp", combo: "pgup"), KeyDef(title: "PgDn", combo: "pgdn"),
         KeyDef(title: "PrtSc", combo: "prtsc"), KeyDef(title: "ScrLk", combo: "scrlk"),
         KeyDef(title: "Pause", combo: "pause"), KeyDef(title: "Space", combo: "space")],
        [KeyDef(title: "Ctrl+Alt+Del", combo: "ctrl+alt+del"), KeyDef(title: "Ctrl+Shift+Esc", combo: "ctrl+shift+esc")],
    ]

    init() {
        let cols = 6
        let btnW: CGFloat = 52
        let btnH: CGFloat = 28
        let spacing: CGFloat = 4
        let padding: CGFloat = 8

        let rowCount = keyLayout.count
        let contentW = CGFloat(cols) * btnW + CGFloat(cols - 1) * spacing + padding * 2
        let contentH = CGFloat(rowCount) * btnH + CGFloat(rowCount - 1) * spacing + padding * 2

        super.init(
            contentRect: NSRect(x: 0, y: 0, width: contentW, height: contentH),
            styleMask: [.titled, .nonactivatingPanel, .utilityWindow, .closable],
            backing: .buffered,
            defer: false
        )

        title = "Combo Keys"
        isMovableByWindowBackground = true
        hidesOnDeactivate = false

        let contentView = self.contentView!
        contentView.wantsLayer = true

        var y = contentH - padding - btnH
        var idx = 0
        for (rowIdx, row) in keyLayout.enumerated() {
            var x = padding
            let isWideRow = rowIdx == keyLayout.count - 1
            let btnWidth = isWideRow ? (contentW - padding * 2 - spacing) / CGFloat(row.count) : btnW

            for key in row {
                let btn = NSButton(frame: NSRect(x: x, y: y, width: btnWidth, height: btnH))
                btn.title = key.title
                btn.bezelStyle = .rounded
                btn.font = NSFont.systemFont(ofSize: 11)
                btn.target = self
                btn.action = #selector(keyPressed(_:))
                btn.tag = idx
                keyCombos.append(key.combo)
                contentView.addSubview(btn)
                x += btnWidth + spacing
                idx += 1
            }
            y -= btnH + spacing
        }
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    @objc private func keyPressed(_ sender: NSButton) {
        guard sender.tag < keyCombos.count else { return }
        let combo = keyCombos[sender.tag]
        comboKeyHandler?(combo)
    }
}
