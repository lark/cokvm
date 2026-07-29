import Foundation
@preconcurrency import AVFoundation
import CoreVideo
import CoreMedia

private let kCMPixelFormat_YUYV: FourCharCode = 0x56595559
private let kCMPixelFormat_NV12: FourCharCode = 0x3230564E
private let kCMPixelFormat_420YpCbCr8BiPlanarFullRange: FourCharCode = 0x7F424346
private let kCMPixelFormat_420YpCbCr8BiPlanarVideoRange: FourCharCode = 0x7F424356
private let kCMPixelFormat_32BGRA: FourCharCode = 0x41524742

protocol VideoCaptureDelegate: AnyObject {
    func onFrameCaptured(_ pixelBuffer: CVPixelBuffer)
    func onVideoReady(_ ready: Bool, format: String)
    func onDevicesChanged(_ devices: [AVCaptureDevice])
}

class VideoCapture: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate, @unchecked Sendable {

    weak var delegate: VideoCaptureDelegate?
    var isReady = false
    var captureSession: AVCaptureSession!
    private var videoDevice: AVCaptureDevice?
    var currentFormat: String = "N/A"

    private var discoverySession: AVCaptureDevice.DiscoverySession?

    init(delegate: VideoCaptureDelegate) {
        self.delegate = delegate
        super.init()
        setupDeviceDiscovery()
    }

    private func setupDeviceDiscovery() {
        #if os(macOS) && compiler(>=5.10)
        if #available(macOS 14.0, *) {
            #if DEBUG
            discoverySession = AVCaptureDevice.DiscoverySession(
                deviceTypes: [.builtInWideAngleCamera, .external],
                mediaType: .video,
                position: .unspecified
            )
            #else
            discoverySession = AVCaptureDevice.DiscoverySession(
                deviceTypes: [.external],
                mediaType: .video,
                position: .unspecified
            )
            #endif
        } else {
            #if DEBUG
            discoverySession = AVCaptureDevice.DiscoverySession(
                deviceTypes: [.builtInWideAngleCamera, .externalUnknown],
                mediaType: .video,
                position: .unspecified
            )
            #else
            discoverySession = AVCaptureDevice.DiscoverySession(
                deviceTypes: [.externalUnknown],
                mediaType: .video,
                position: .unspecified
            )
            #endif
        }
        #else
        #if DEBUG
        discoverySession = AVCaptureDevice.DiscoverySession(
            deviceTypes: [.builtInWideAngleCamera, .externalUnknown],
            mediaType: .video,
            position: .unspecified
        )
        #else
        discoverySession = AVCaptureDevice.DiscoverySession(
            deviceTypes: [.externalUnknown],
            mediaType: .video,
            position: .unspecified
        )
        #endif
        #endif

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(onDevicesDidChange),
            name: .AVCaptureDeviceWasConnected,
            object: nil
        )

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(onDevicesDidChange),
            name: .AVCaptureDeviceWasDisconnected,
            object: nil
        )
    }

    @objc private func onDevicesDidChange() {
        DispatchQueue.main.async { [weak self] in
            guard let self = self else { return }
            let devices = self.discoverySession?.devices ?? []
            self.delegate?.onDevicesChanged(devices)

            if self.isReady {
                if let currentDevice = self.videoDevice, !devices.contains(currentDevice) {
                    self.stop()
                }
            }
        }
    }

    func availableDevices() -> [AVCaptureDevice] {
        return discoverySession?.devices ?? []
    }

    private func selectBestFormat(for device: AVCaptureDevice) -> AVCaptureDevice.Format? {
        var bestFormat: AVCaptureDevice.Format?
        var bestScore = 0

        for format in device.formats {
            let dimensions = CMVideoFormatDescriptionGetDimensions(format.formatDescription)
            let width = dimensions.width
            let height = dimensions.height

            let fps = format.videoSupportedFrameRateRanges.first
            let minFps = fps?.minFrameRate ?? 0
            let maxFps = fps?.maxFrameRate ?? 0

            let pixelFormat = CMFormatDescriptionGetMediaSubType(format.formatDescription)

            var score = 0

            if width == 1920 && height == 1080 {
                score += 100
            } else if width == 1280 && height == 720 {
                score += 50
            }

            if pixelFormat == kCMPixelFormat_NV12 {
                score += 50
            } else if pixelFormat == kCMPixelFormat_YUYV {
                score += 40
            } else if pixelFormat == kCMPixelFormat_420YpCbCr8BiPlanarFullRange ||
                      pixelFormat == kCMPixelFormat_420YpCbCr8BiPlanarVideoRange {
                score += 25
            }

            if minFps <= 30 && maxFps >= 30 {
                score += 30
            }

            if score > bestScore {
                bestScore = score
                bestFormat = format
            }
        }

        return bestFormat
    }

    private func formatDescription(for format: AVCaptureDevice.Format) -> String {
        let dimensions = CMVideoFormatDescriptionGetDimensions(format.formatDescription)
        let fps = format.videoSupportedFrameRateRanges.first
        let pixelFormat = CMFormatDescriptionGetMediaSubType(format.formatDescription)

        var formatName: String
        switch pixelFormat {
        case kCMPixelFormat_NV12:
            formatName = "NV12"
        case kCMPixelFormat_YUYV:
            formatName = "YUY2"
        case kCMPixelFormat_420YpCbCr8BiPlanarFullRange:
            formatName = "YUV420"
        case kCMPixelFormat_420YpCbCr8BiPlanarVideoRange:
            formatName = "YUV420"
        case kCMPixelFormat_32BGRA:
            formatName = "BGRA"
        default:
            formatName = fourCharCodeToString(pixelFormat)
        }

        let fpsValue = fps != nil ? String(format: "%.0ffps", 30.0) : "?"

        return "\(dimensions.width)x\(dimensions.height) \(formatName) \(fpsValue)"
    }

    private func fourCharCodeToString(_ code: FourCharCode) -> String {
        let bytes = [
            UInt8((code >> 24) & 0xFF),
            UInt8((code >> 16) & 0xFF),
            UInt8((code >> 8) & 0xFF),
            UInt8(code & 0xFF)
        ]
        if let str = String(bytes: bytes, encoding: .ascii), str.rangeOfCharacter(from: .controlCharacters) == nil {
            return str
        }
        return String(format: "0x%08X", code)
    }

    func start(with device: AVCaptureDevice? = nil) {
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            guard let self = self else { return }

            self.captureSession = AVCaptureSession()

            let selectedDevice: AVCaptureDevice
            if let device = device {
                selectedDevice = device
            } else {
                #if DEBUG
                if let defaultDevice = AVCaptureDevice.default(
                    .builtInWideAngleCamera,
                    for: .video,
                    position: .front
                ) {
                    selectedDevice = defaultDevice
                } else if let firstDevice = self.availableDevices().first {
                    selectedDevice = firstDevice
                } else {
                    DispatchQueue.main.async {
                        self.delegate?.onVideoReady(false, format: "No device")
                    }
                    return
                }
                #else
                if let firstDevice = self.availableDevices().first {
                    selectedDevice = firstDevice
                } else {
                    DispatchQueue.main.async {
                        self.delegate?.onVideoReady(false, format: "No device")
                    }
                    return
                }
                #endif
            }

            self.videoDevice = selectedDevice

            do {
                try selectedDevice.lockForConfiguration()

                if let bestFormat = self.selectBestFormat(for: selectedDevice) {
                    selectedDevice.activeFormat = bestFormat

                    if let fpsRange = bestFormat.videoSupportedFrameRateRanges.first {
                        selectedDevice.activeVideoMinFrameDuration = fpsRange.minFrameDuration
                        selectedDevice.activeVideoMaxFrameDuration = fpsRange.maxFrameDuration
                    } else {
                        selectedDevice.activeVideoMinFrameDuration = CMTimeMake(value: 1, timescale: 30)
                        selectedDevice.activeVideoMaxFrameDuration = CMTimeMake(value: 1, timescale: 30)
                    }

                    self.currentFormat = self.formatDescription(for: bestFormat)
                }

                selectedDevice.unlockForConfiguration()

                let input = try AVCaptureDeviceInput(device: selectedDevice)
                if self.captureSession.canAddInput(input) {
                    self.captureSession.addInput(input)
                }

                let output = AVCaptureVideoDataOutput()
                output.setSampleBufferDelegate(self, queue: DispatchQueue(label: "videoQueue"))

                if let connection = output.connection(with: .video) {
                    if #available(macOS 14.0, *) {
                        let portraitAngle = CGFloat(Double.pi / 2.0)
                        if connection.isVideoRotationAngleSupported(portraitAngle) {
                            connection.videoRotationAngle = portraitAngle
                        }
                    }
                }

                if self.captureSession.canAddOutput(output) {
                    self.captureSession.addOutput(output)
                }

                self.captureSession.startRunning()
                self.isReady = true
                DispatchQueue.main.async {
                    self.delegate?.onVideoReady(true, format: self.currentFormat)
                }
            } catch {
                DispatchQueue.main.async {
                    self.delegate?.onVideoReady(false, format: "Error: \(error.localizedDescription)")
                }
            }
        }
    }

    func stop() {
        if captureSession?.isRunning ?? false {
            captureSession.stopRunning()
        }
        isReady = false
        delegate?.onVideoReady(false, format: "Stopped")
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        delegate?.onFrameCaptured(pixelBuffer)
    }
}
