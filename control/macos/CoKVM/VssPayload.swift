import Foundation

struct VssPayload {
    var type: UInt8
    var data: Data

    private init(type: UInt8, data: Data) {
        self.type = type
        self.data = data
    }

    static func keyboard(modifiers: UInt8, keycodes: [UInt8]) -> VssPayload {
        var data = Data()
        data.append(modifiers)
        data.append(0x00)
        var kcs = keycodes
        while kcs.count < 6 { kcs.append(0) }
        kcs = Array(kcs.prefix(6))
        data.append(contentsOf: kcs)
        while data.count < 8 { data.append(0) }
        return VssPayload(type: 0x01, data: data)
    }

    static func mouse(buttons: UInt8, x: Int16, y: Int16, wheel: Int8) -> VssPayload {
        var data = Data()
        data.append(buttons)
        data.append(contentsOf: withUnsafeBytes(of: x.littleEndian) { Array($0) })
        data.append(contentsOf: withUnsafeBytes(of: y.littleEndian) { Array($0) })
        data.append(UInt8(bitPattern: wheel))
        while data.count < 8 { data.append(0) }
        return VssPayload(type: 0x02, data: data)
    }

    static func ledStatus(capsLock: Bool) -> VssPayload {
        var data = Data()
        let ledByte: UInt8 = capsLock ? 0x02 : 0x00
        data.append(ledByte)
        while data.count < 8 { data.append(0) }
        return VssPayload(type: 0x03, data: data)
    }

    var payload: Data {
        var d = Data()
        d.append(0x4B)
        d.append(0x4D)
        d.append(0x00)
        d.append(type)
        d.append(data)
        d.append(contentsOf: [UInt8](repeating: 0, count: 10))
        return d
    }
}
