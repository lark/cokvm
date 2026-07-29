import Foundation

protocol MouseManagerDelegate: AnyObject {
    func onMouseMove(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, _ buttons: UInt8)
}

class MouseManager {
    
    weak var delegate: MouseManagerDelegate?
    private var lastWinPos: (Int32, Int32) = (0, 0)
    private var lastMappedPos: (Int16, Int16) = (0, 0)
    private var lastButtons: UInt8 = 0
    
    init(delegate: MouseManagerDelegate) {
        self.delegate = delegate
    }
    
    func getLastWinPos() -> (Int32, Int32) {
        return lastWinPos
    }
    
    func getLastMappedPos() -> (Int16, Int16) {
        return lastMappedPos
    }
    
    func onMouseMove(_ x: CGFloat, _ y: CGFloat, _ width: CGFloat, _ height: CGFloat, _ buttons: UInt8) -> VssPayload? {
        let hidPos = MouseManager.convertToHID(x: x, y: y, width: width, height: height)

        lastWinPos = (Int32(x), Int32(y))
        lastMappedPos = hidPos
        lastButtons = buttons

        return VssPayload.mouse(buttons: buttons, x: hidPos.0, y: hidPos.1, wheel: 0)
    }

    func onMouseWheel(_ deltaY: CGFloat) -> VssPayload? {
        // X/Y are absolute: repeat the last position and buttons instead of (0, 0),
        // otherwise the pointer jumps to the origin and held buttons are released.
        return VssPayload.mouse(buttons: lastButtons, x: lastMappedPos.0, y: lastMappedPos.1,
                                wheel: Int8(clamping: Int(deltaY * 120)))
    }
    
    static func convertToHID(x: CGFloat, y: CGFloat, width: CGFloat, height: CGFloat) -> (Int16, Int16) {
        let hidX = Int16(x / width * 17280.0)
        let hidY = Int16((height - y) / height * 17280.0)
        
        let clampedX = max(min(hidX, 17280), 0)
        let clampedY = max(min(hidY, 17280), 0)
        
        return (clampedX, clampedY)
    }
}
