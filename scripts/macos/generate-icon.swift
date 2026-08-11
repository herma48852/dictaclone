import AppKit
import Foundation

guard CommandLine.arguments.count == 2 else {
    fputs("usage: generate-icon.swift OUTPUT.png\n", stderr)
    exit(64)
}

let scale: CGFloat = 8
let size = NSSize(width: 128 * scale, height: 128 * scale)
let image = NSImage(size: size)
image.lockFocus()

guard let context = NSGraphicsContext.current?.cgContext else {
    fputs("failed to create icon graphics context\n", stderr)
    exit(1)
}
context.scaleBy(x: scale, y: scale)

NSColor(calibratedRed: 0.19, green: 0.34, blue: 0.78, alpha: 1).setFill()
NSBezierPath(
    roundedRect: NSRect(x: 0, y: 0, width: 128, height: 128),
    xRadius: 28,
    yRadius: 28).fill()

NSColor.white.setFill()
NSBezierPath(roundedRect: NSRect(x: 50, y: 48, width: 28, height: 56), xRadius: 14, yRadius: 14).fill()

NSColor.white.setStroke()
let cradle = NSBezierPath()
cradle.lineWidth = 10
cradle.lineCapStyle = .round
cradle.move(to: NSPoint(x: 34, y: 66))
cradle.curve(to: NSPoint(x: 94, y: 66), controlPoint1: NSPoint(x: 34, y: 42), controlPoint2: NSPoint(x: 94, y: 42))
cradle.stroke()

let stand = NSBezierPath()
stand.lineWidth = 10
stand.lineCapStyle = .round
stand.move(to: NSPoint(x: 64, y: 42))
stand.line(to: NSPoint(x: 64, y: 22))
stand.move(to: NSPoint(x: 48, y: 22))
stand.line(to: NSPoint(x: 80, y: 22))
stand.stroke()

image.unlockFocus()

guard
    let tiff = image.tiffRepresentation,
    let bitmap = NSBitmapImageRep(data: tiff),
    let png = bitmap.representation(using: .png, properties: [:])
else {
    fputs("failed to render icon\n", stderr)
    exit(1)
}

try png.write(to: URL(fileURLWithPath: CommandLine.arguments[1]), options: .atomic)
