import Cocoa

protocol KeyboardManagerDelegate: AnyObject {
    func onKeyDown(_ scancode: UInt8)
    func onKeyUp(_ scancode: UInt8)
    func onMouseWheel(_ deltaY: CGFloat)
}

class KeyboardManager {
    
    weak var delegate: KeyboardManagerDelegate?
    private var modifiers: UInt8 = 0
    private var activeKeycodes: [UInt8] = [0, 0, 0, 0, 0, 0]
    
    init(delegate: KeyboardManagerDelegate) {
        self.delegate = delegate
    }
    
    func getModifiers() -> UInt8 {
        return modifiers
    }
    
    func getActiveKeycodes() -> [UInt8] {
        return activeKeycodes
    }
    
    func onKeyDown(_ keyCode: UInt8, modifierFlags: NSEvent.ModifierFlags) -> VssPayload? {
        updateModifiers(modifierFlags)

        if !macosModifierKeycodes.contains(keyCode) {
            let scancode = keyCodeToScanCode(keyCode)
            addKeycode(scancode)
        }

        return createPayload()
    }

    func onKeyUp(_ keyCode: UInt8, modifierFlags: NSEvent.ModifierFlags) -> VssPayload? {
        updateModifiers(modifierFlags)

        if !macosModifierKeycodes.contains(keyCode) {
            let scancode = keyCodeToScanCode(keyCode)
            removeKeycode(scancode)
        }

        return createPayload()
    }
    
    func updateModifiers(_ flags: NSEvent.ModifierFlags) {
        modifiers = 0

        if flags.contains(.control) {
            modifiers |= 0x01
        }
        if flags.contains(.shift) {
            modifiers |= 0x02
        }
        if flags.contains(.option) {
            modifiers |= 0x04
        }
        if flags.contains(.command) {
            modifiers |= 0x08
        }
    }
    
    private func addKeycode(_ scancode: UInt8) {
        for i in 0..<activeKeycodes.count {
            if activeKeycodes[i] == 0 {
                activeKeycodes[i] = scancode
                return
            }
        }
    }
    
    private func removeKeycode(_ scancode: UInt8) {
        for i in 0..<activeKeycodes.count {
            if activeKeycodes[i] == scancode {
                activeKeycodes[i] = 0
                break
            }
        }
    }
    
    private func createPayload() -> VssPayload {
        return VssPayload.keyboard(modifiers: modifiers, keycodes: activeKeycodes)
    }
}
