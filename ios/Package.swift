// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MagerCamProtocol",
    platforms: [.macOS(.v13), .iOS(.v17)],
    products: [.library(name: "MagerCamProtocol", targets: ["MagerCamProtocol"])],
    targets: [
        .target(name: "MagerCamProtocol", path: "Sources/Protocol"),
        .testTarget(name: "MagerCamProtocolTests", dependencies: ["MagerCamProtocol"], path: "Tests")
    ]
)
