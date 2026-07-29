import Foundation

func testMouseManagerConvertToHID() {
    print("Testing MouseManager.convertToHID...")
    
    let width: CGFloat = 1280
    let height: CGFloat = 720
    
    let testCases = [
        ("bottom-left", (0, 0), (Int16(0), Int16(17240))),
        ("top-left", (0, 720), (Int16(0), Int16(0))),
        ("bottom-right", (1280, 0), (Int16(17240), Int16(17240))),
        ("top-right", (1280, 720), (Int16(17240), Int16(0))),
        ("center", (640, 360), (Int16(8620), Int16(8620))),
        ("quarter-top", (640, 540), (Int16(8620), Int16(4310))),
        ("quarter-bottom", (640, 180), (Int16(8620), Int16(12930))),
    ]
    
    var allPassed = true
    for (name, (x, y), expected) in testCases {
        let result = MouseManager.convertToHID(x: CGFloat(x), y: CGFloat(y), width: width, height: height)
        
        let xDiff = abs(result.0 - expected.0)
        let yDiff = abs(result.1 - expected.1)
        
        let passed = xDiff <= 1 && yDiff <= 1
        
        if passed {
            print("✓ \(name): (\(x), \(y)) -> (\(result.0), \(result.1))")
        } else {
            print("✗ \(name): (\(x), \(y)) -> (\(result.0), \(result.1)) expected (\(expected.0), \(expected.1))")
            allPassed = false
        }
    }
    
    if allPassed {
        print("All tests passed!")
    } else {
        print("Some tests failed!")
    }
}
