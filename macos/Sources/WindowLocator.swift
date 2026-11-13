import AppKit
import CoreGraphics

enum OverlayLayout {
    static let dragHeight: CGFloat = 40
    static let resizeHitMargin: CGFloat = 8
    static let headerLeftGuard: CGFloat = 220
    static let headerRightGuard: CGFloat = 110
    // Visual coverage is independent of input routing: no titlebar/edge gaps in the picture.
    static func visualFrame(in window: NSRect) -> NSRect { window }
    static func shields(in window: NSRect) -> [NSRect] {
        let edge = resizeHitMargin, top = dragHeight
        return [
            NSRect(x: window.minX+edge,y: window.minY+edge,width: max(1,window.width-2*edge),height: max(1,window.height-top-edge)),
            NSRect(x: window.minX+edge,y: window.maxY-top,width: min(headerLeftGuard-edge,window.width-2*edge),height: top-edge),
            NSRect(x: window.maxX-headerRightGuard,y: window.maxY-top,width: headerRightGuard-edge,height: top-edge)
        ]
    }
    static func skipFrame(in window: NSRect) -> NSRect {
        NSRect(x: window.maxX-98,y: window.maxY-43,width: 84,height: 31)
    }
}

struct ClientWindow: Equatable {
    let id: CGWindowID
    let pid: pid_t
    let quartzBounds: CGRect
    func appKitFrame(primaryTop: CGFloat) -> NSRect {
        NSRect(x: quartzBounds.minX, y: primaryTop - quartzBounds.maxY,
               width: quartzBounds.width, height: quartzBounds.height)
    }
}

enum WindowLocator {
    // Window metadata only: no screenshots, Accessibility prompts, or window changes.
    static func select(_ rows: [[String: Any]], pid: pid_t, preferredID: CGWindowID? = nil) -> ClientWindow? {
        let candidates = rows.compactMap { row -> ClientWindow? in
            guard let owner = row[kCGWindowOwnerPID as String] as? Int32, owner == pid,
                  (row[kCGWindowLayer as String] as? Int) == 0,
                  (row[kCGWindowIsOnscreen as String] as? Bool) == true,
                  (row[kCGWindowAlpha as String] as? Double ?? 1) > 0.01,
                  let id = row[kCGWindowNumber as String] as? UInt32,
                  let dictionary = row[kCGWindowBounds as String] as? [String: Any],
                  let bounds = CGRect(dictionaryRepresentation: dictionary as CFDictionary),
                  bounds.width >= 320, bounds.height >= 200,
                  bounds.origin.x.isFinite, bounds.origin.y.isFinite,
                  bounds.width.isFinite, bounds.height.isFinite else { return nil }
            return ClientWindow(id: id, pid: owner, quartzBounds: bounds)
        }
        // Keep following the original window. Never jump to a different Codex window mid-animation.
        if let preferredID { return candidates.first { $0.id == preferredID } }
        return candidates.first
    }

    static func find(pid: pid_t, preferredID: CGWindowID? = nil) -> ClientWindow? {
        let rows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
        return select(rows, pid: pid, preferredID: preferredID)
    }

    static var primaryTop: CGFloat { NSScreen.screens.first?.frame.maxY ?? CGDisplayBounds(CGMainDisplayID()).height }
}

func runWindowTests() throws -> [String] {
    func row(_ id: UInt32, _ pid: Int32 = 12, _ layer: Int = 0, _ visible: Bool = true, _ bounds: CGRect = CGRect(x: 221, y: 44, width: 1249, height: 820)) -> [String: Any] {
        [kCGWindowNumber as String: id, kCGWindowOwnerPID as String: pid,
         kCGWindowLayer as String: layer, kCGWindowIsOnscreen as String: visible,
         kCGWindowBounds as String: bounds.dictionaryRepresentation]
    }
    var passed = [String]()
    func check(_ value: @autoclosure () -> Bool, _ name: String) throws {
        guard value() else { throw OptionsError.invalid("FAIL: \(name)") }; passed.append(name)
    }
    let normal = row(100)
    let selected = WindowLocator.select([row(1, 22), row(2, 12, 3), row(3, 12, 0, false), row(4, 12, 0, true, CGRect(x: 0,y: 0,width: 168,height: 160)), normal], pid: 12)
    try check(selected?.id == 100, "ignore other apps, panels, hidden windows and tiny helper windows")
    try check(selected?.appKitFrame(primaryTop: 956) == CGRect(x: 221,y: 92,width: 1249,height: 820), "convert Quartz origin without changing window dimensions")
    try check(WindowLocator.select([normal, row(101)], pid: 12)?.id == 100, "choose frontmost matching client window")
    try check(WindowLocator.select([normal, row(101)], pid: 12, preferredID: 101)?.id == 101, "keep chosen window when stacking order changes")
    try check(WindowLocator.select([normal], pid: 12, preferredID: 101) == nil, "missing chosen window never falls back to another window")
    try check(WindowLocator.select([], pid: 12) == nil, "missing window never falls back to the screen")
    let secondary = row(102,12,0,true,CGRect(x: -1200,y: -800,width: 1000,height: 700))
    try check(WindowLocator.select([secondary],pid: 12)?.appKitFrame(primaryTop: 956) == CGRect(x: -1200,y: 1056,width: 1000,height: 700), "preserve coordinates on a secondary screen")
    let resized = row(100,12,0,true,CGRect(x: 99,y: 121,width: 900,height: 650))
    try check(WindowLocator.select([resized],pid: 12,preferredID: 100)?.quartzBounds == CGRect(x: 99,y: 121,width: 900,height: 650), "follow size and position of the same window")
    let parent = NSRect(x: 221,y: 92,width: 1249,height: 820)
    try check(OverlayLayout.visualFrame(in: parent) == parent, "visual frame covers the complete client including titlebar and all edges")
    let shields = OverlayLayout.shields(in: parent)
    try check(!shields.contains { $0.contains(NSPoint(x:parent.midX,y:parent.maxY-24)) }, "native titlebar drag area receives mouse events without a visual gap")
    try check(!shields.contains { $0.contains(NSPoint(x:parent.minX+2,y:parent.midY)) }, "native resize edge receives mouse events without a visual gap")
    try check(shields.contains { $0.contains(NSPoint(x:parent.midX,y:parent.midY)) }, "shield blocks hidden Codex content controls")
    try check(shields.contains { $0.contains(NSPoint(x:parent.minX+35,y:parent.maxY-24)) }, "shield blocks invisible original close and navigation controls")
    try check(shields.contains { $0.contains(NSPoint(x:parent.minX+165,y:parent.maxY-24)) }, "shield also blocks hidden original sidebar and forward controls")
    try check(parent.contains(OverlayLayout.skipFrame(in: parent)), "skip control remains inside full window coverage")
    return passed
}
