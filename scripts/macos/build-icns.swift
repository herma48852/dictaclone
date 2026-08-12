import AppKit
import Foundation

guard CommandLine.arguments.count == 3 else {
    fputs("usage: build-icns.swift SOURCE_PNG OUTPUT_ICNS\n", stderr)
    exit(64)
}

let sourcePath = CommandLine.arguments[1]
let outputPath = CommandLine.arguments[2]

guard let source = NSImage(contentsOfFile: sourcePath) else {
    fputs("failed to read icon source: \(sourcePath)\n", stderr)
    exit(66)
}

let representations: [(type: String, pixels: Int)] = [
    ("icp4", 16),
    ("icp5", 32),
    ("icp6", 64),
    ("ic07", 128),
    ("ic08", 256),
    ("ic09", 512),
    ("ic10", 1024),
    ("ic11", 32),
    ("ic12", 64),
    ("ic13", 256),
    ("ic14", 512),
]

func bigEndianBytes(_ value: UInt32) -> Data {
    var encoded = value.bigEndian
    return Data(bytes: &encoded, count: MemoryLayout<UInt32>.size)
}

func pngRepresentation(pixels: Int) -> Data? {
    guard let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: pixels,
        pixelsHigh: pixels,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0)
    else {
        return nil
    }

    bitmap.size = NSSize(width: pixels, height: pixels)
    NSGraphicsContext.saveGraphicsState()
    defer { NSGraphicsContext.restoreGraphicsState() }

    guard let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
        return nil
    }

    NSGraphicsContext.current = context
    context.imageInterpolation = .high
    source.draw(
        in: NSRect(x: 0, y: 0, width: pixels, height: pixels),
        from: NSRect(origin: .zero, size: source.size),
        operation: .copy,
        fraction: 1)
    context.flushGraphics()
    return bitmap.representation(using: .png, properties: [:])
}

var elements = Data()
for representation in representations {
    guard
        let type = representation.type.data(using: .ascii),
        type.count == 4,
        let png = pngRepresentation(pixels: representation.pixels)
    else {
        fputs(
            "failed to render \(representation.pixels)-pixel icon\n",
            stderr)
        exit(70)
    }

    elements.append(type)
    elements.append(bigEndianBytes(UInt32(png.count + 8)))
    elements.append(png)
}

var container = Data("icns".utf8)
container.append(bigEndianBytes(UInt32(elements.count + 8)))
container.append(elements)

do {
    try FileManager.default.createDirectory(
        at: URL(fileURLWithPath: outputPath).deletingLastPathComponent(),
        withIntermediateDirectories: true)
    try container.write(
        to: URL(fileURLWithPath: outputPath),
        options: .atomic)
} catch {
    fputs("failed to write icon: \(error)\n", stderr)
    exit(70)
}
