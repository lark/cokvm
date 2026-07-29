import Cocoa

enum VideoStatus {
    case noDevice
    case singleSourceActive
    case singleSourcePaused
    case multiSourceActive
    case multiSourcePaused
}

class StatusBarView: NSView {

    private var viewportLabel: NSTextField!
    private var mouseLabel: NSTextField!
    private var keyboardLabel: NSTextField!
    private var videoStatusButton: NSButton!
    private var btStatusButton: NSButton!
    private var capsLockButton: NSButton!

    var videoButtonAction: (() -> Void)?
    var videoLongPressAction: (() -> Void)?
    var btButtonAction: (() -> Void)?

    private var videoStatus: VideoStatus = .noDevice
    private var longPressTimer: Timer?
    private var isLongPress = false

    func isVideoActive() -> Bool {
        return videoStatus == .singleSourceActive || videoStatus == .multiSourceActive
    }

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
        layer?.backgroundColor = NSColor.controlBackgroundColor.cgColor

        viewportLabel = createLabel()
        viewportLabel.translatesAutoresizingMaskIntoConstraints = false
        viewportLabel.stringValue = "---"
        addSubview(viewportLabel)

        mouseLabel = createLabel()
        mouseLabel.translatesAutoresizingMaskIntoConstraints = false
        mouseLabel.stringValue = "---"
        addSubview(mouseLabel)

        keyboardLabel = createLabel()
        keyboardLabel.translatesAutoresizingMaskIntoConstraints = false
        keyboardLabel.stringValue = "---"
        addSubview(keyboardLabel)

        videoStatusButton = createStatusButton(imageName: "questionmark.video.fill")
        videoStatusButton.translatesAutoresizingMaskIntoConstraints = false
        videoStatusButton.target = self
        videoStatusButton.action = #selector(videoButtonClicked(_:))
        videoStatusButton.sendAction(on: [.leftMouseDown, .leftMouseUp])
        addSubview(videoStatusButton)

        btStatusButton = createStatusButton(imageName: "dot.radiowaves.up.forward")
        btStatusButton.translatesAutoresizingMaskIntoConstraints = false
        btStatusButton.target = self
        btStatusButton.action = #selector(btButtonClicked(_:))
        addSubview(btStatusButton)

        capsLockButton = createStatusButton(imageName: "capslock")
        capsLockButton.translatesAutoresizingMaskIntoConstraints = false
        capsLockButton.isBordered = false
        addSubview(capsLockButton)

        NSLayoutConstraint.activate([
            viewportLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            viewportLabel.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 10),

            mouseLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            mouseLabel.leadingAnchor.constraint(equalTo: viewportLabel.trailingAnchor, constant: 15),

            keyboardLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            keyboardLabel.leadingAnchor.constraint(equalTo: mouseLabel.trailingAnchor, constant: 15),

            btStatusButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            btStatusButton.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -10),
            btStatusButton.widthAnchor.constraint(equalToConstant: 20),
            btStatusButton.heightAnchor.constraint(equalToConstant: 20),

            videoStatusButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            videoStatusButton.trailingAnchor.constraint(equalTo: btStatusButton.leadingAnchor, constant: -10),
            videoStatusButton.widthAnchor.constraint(equalToConstant: 20),
            videoStatusButton.heightAnchor.constraint(equalToConstant: 20),

            capsLockButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            capsLockButton.trailingAnchor.constraint(equalTo: videoStatusButton.leadingAnchor, constant: -10),
            capsLockButton.widthAnchor.constraint(equalToConstant: 20),
            capsLockButton.heightAnchor.constraint(equalToConstant: 20)
        ])
    }

    private func createLabel() -> NSTextField {
        let label = NSTextField(labelWithString: "")
        label.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)
        label.textColor = NSColor.labelColor
        label.isEditable = false
        label.isBordered = false
        label.drawsBackground = false
        label.lineBreakMode = .byTruncatingTail
        label.setContentHuggingPriority(.defaultLow, for: .horizontal)
        label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        return label
    }

    private func createStatusButton(imageName: String) -> NSButton {
        let button = NSButton()
        button.setButtonType(.momentaryPushIn)
        button.isBordered = false
        button.title = ""

        if let symbolImage = NSImage(systemSymbolName: imageName, accessibilityDescription: nil) {
            button.image = symbolImage
        } else {
            button.image = NSImage(named: imageName)
        }

        button.image?.size = NSSize(width: 16, height: 16)
        button.imageScaling = .scaleProportionallyUpOrDown
        button.setContentHuggingPriority(.defaultHigh, for: .horizontal)
        button.setContentCompressionResistancePriority(.defaultHigh, for: .horizontal)
        return button
    }

    @objc private func videoButtonClicked(_ sender: NSButton) {
        let event = NSApp.currentEvent!

        if event.type == .leftMouseDown {
            isLongPress = false
            longPressTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: false) { [weak self] _ in
                guard let self = self else { return }
                self.isLongPress = true
                self.videoLongPressAction?()
            }
        } else if event.type == .leftMouseUp {
            longPressTimer?.invalidate()
            if !isLongPress {
                videoButtonAction?()
            }
        }
    }

    @objc private func btButtonClicked(_ sender: NSButton) {
        btButtonAction?()
    }

    func setVideoStatus(_ status: VideoStatus) {
        videoStatus = status

        let imageName: String
        let isReady: Bool

        switch status {
        case .noDevice:
            imageName = "video.fill"
            isReady = false
        case .singleSourceActive:
            imageName = "video.fill"
            isReady = true
        case .singleSourcePaused:
            imageName = "video.slash.fill"
            isReady = true
        case .multiSourceActive:
            imageName = "video.fill.badge.plus"
            isReady = true
        case .multiSourcePaused:
            imageName = "questionmark.video.fill"
            isReady = true
        }

        if let symbolImage = NSImage(systemSymbolName: imageName, accessibilityDescription: nil) {
            videoStatusButton.image = symbolImage
            videoStatusButton.image?.size = NSSize(width: 16, height: 16)
        }

        videoStatusButton.image?.isTemplate = true
        videoStatusButton.contentTintColor = isReady ? NSColor.systemGreen : NSColor.systemGray
    }

    func getVideoButtonPosition() -> NSPoint {
        return videoStatusButton.convert(videoStatusButton.bounds.origin, to: nil)
    }

    func update(
        viewportSize: (CGFloat, CGFloat),
        physicalSize: (CGFloat, CGFloat),
        mousePos: (Int32, Int32),
        mappedPos: (Int16, Int16),
        modifiers: UInt8,
        activeKeycodes: [UInt8],
        btReady: Bool,
        btDiscovered: Bool,
        capsLockOn: Bool
    ) {
        viewportLabel.stringValue = String(format: "%.0fx%.0f @ %.0fx%.0f",
                                           viewportSize.0, viewportSize.1,
                                           physicalSize.0, physicalSize.1)

        mouseLabel.stringValue = String(format: "(%4d, %4d) -> (%5d, %5d)",
                                        mousePos.0, mousePos.1, mappedPos.0, mappedPos.1)

        let modifierStr = formatModifiers(modifiers)
        let activeKeys = activeKeycodes.filter { $0 != 0 }.map { String(format: "0x%02X", $0) }
        keyboardLabel.stringValue = "\(modifierStr) | [\(activeKeys.joined(separator: ", "))]"

        if btReady {
            if let symbolImage = NSImage(systemSymbolName: "dot.radiowaves.up.forward", accessibilityDescription: nil) {
                btStatusButton.image = symbolImage
            }
            btStatusButton.contentTintColor = NSColor.systemGreen
        } else if btDiscovered {
            if let symbolImage = NSImage(systemSymbolName: "dot.radiowaves.up", accessibilityDescription: nil) {
                btStatusButton.image = symbolImage
            }
            btStatusButton.contentTintColor = NSColor.systemYellow
        } else {
            if let symbolImage = NSImage(systemSymbolName: "dot.radiowaves.up.forward", accessibilityDescription: nil) {
                btStatusButton.image = symbolImage
            }
            btStatusButton.contentTintColor = NSColor.systemGray
        }
        btStatusButton.image?.isTemplate = true
        btStatusButton.image?.size = NSSize(width: 16, height: 16)

        let capsLockImageName = capsLockOn ? "capslock.fill" : "capslock"
        if let symbolImage = NSImage(systemSymbolName: capsLockImageName, accessibilityDescription: nil) {
            capsLockButton.image = symbolImage
            capsLockButton.image?.size = NSSize(width: 16, height: 16)
            capsLockButton.image?.isTemplate = true
            capsLockButton.contentTintColor = capsLockOn ? NSColor.systemGreen : NSColor.systemGray
        }
    }

    private func formatModifiers(_ modifiers: UInt8) -> String {
        var parts = [String]()

        if (modifiers & 0x01) != 0 { parts.append("LC") }
        if (modifiers & 0x02) != 0 { parts.append("LS") }
        if (modifiers & 0x04) != 0 { parts.append("LA") }
        if (modifiers & 0x08) != 0 { parts.append("LG") }
        if (modifiers & 0x10) != 0 { parts.append("RC") }
        if (modifiers & 0x20) != 0 { parts.append("RS") }
        if (modifiers & 0x40) != 0 { parts.append("RA") }
        if (modifiers & 0x80) != 0 { parts.append("RG") }

        return parts.isEmpty ? "NONE" : parts.joined(separator: "+")
    }
}
