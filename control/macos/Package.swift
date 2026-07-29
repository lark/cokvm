// swift-tools-version: 5.10

import PackageDescription

let package = Package(
    name: "CoKVM",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "CoKVM", targets: ["CoKVM"])
    ],
    dependencies: [],
    targets: [
        .executableTarget(
            name: "CoKVM",
            dependencies: [],
            path: "CoKVM",
            exclude: ["Info.plist"],
            swiftSettings: [],
            linkerSettings: [
                .linkedFramework("Foundation"),
                .linkedFramework("Cocoa"),
                .linkedFramework("AVFoundation"),
                .linkedFramework("CoreBluetooth"),
                .linkedFramework("CoreMedia"),
                .linkedFramework("CoreVideo"),
                .linkedFramework("QuartzCore"),
                .linkedFramework("CoreImage")
            ]
        )
    ]
)
