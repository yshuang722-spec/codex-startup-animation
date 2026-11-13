import AppKit
import WebKit

final class PassivePanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class InputShield: NSView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) { }
    override func rightMouseDown(with event: NSEvent) { }
    override func otherMouseDown(with event: NSEvent) { }
    override func scrollWheel(with event: NSEvent) { }
}

final class SkipControl: NSView {
    var onSkip: (() -> Void)?
    override var isFlipped: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) { onSkip?() }
    override func accessibilityPerformPress() -> Bool { onSkip?(); return true }
    override func draw(_ dirtyRect: NSRect) {
        let shape = NSBezierPath(roundedRect:bounds.insetBy(dx:0.5,dy:0.5),xRadius:8,yRadius:8)
        NSColor(calibratedRed:2/255,green:18/255,blue:14/255,alpha:0.95).setFill(); shape.fill()
        NSColor(calibratedRed:87/255,green:1,blue:169/255,alpha:0.45).setStroke(); shape.lineWidth=1; shape.stroke()
        let label = NSAttributedString(string:"跳过动画",attributes:[.font:NSFont.systemFont(ofSize:12),.foregroundColor:NSColor(calibratedRed:184/255,green:246/255,blue:210/255,alpha:1)])
        let size = label.size()
        label.draw(at:NSPoint(x:(bounds.width-size.width)/2,y:(bounds.height-size.height)/2))
    }
}

// The full visual window is click-through. Small transparent shields block hidden content actions,
// while native titlebar/edge input reaches Codex. Child windows move atomically with the visual parent.
final class OverlayPresenter {
    let visual: PassivePanel
    private let shields: [PassivePanel]
    private let skipPanel: PassivePanel
    private let skipControl: SkipControl
    private(set) var visible = false
    var frame: NSRect { visual.frame }

    init(web: WKWebView,onSkip: @escaping () -> Void) {
        func panel() -> PassivePanel {
            let window = PassivePanel(contentRect:NSRect(x:0,y:0,width:1,height:1),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
            window.isOpaque=false; window.backgroundColor = .clear; window.hasShadow=false
            window.level = .floating; window.collectionBehavior=[.fullScreenAuxiliary]
            window.hidesOnDeactivate=false; window.isReleasedWhenClosed=false
            return window
        }
        visual=panel(); visual.ignoresMouseEvents=true; visual.contentView=web
        web.wantsLayer=true; web.layer?.cornerRadius=12; web.layer?.masksToBounds=true
        shields=(0..<3).map { _ in
            let shield=panel()
            shield.backgroundColor=NSColor.black.withAlphaComponent(0.001)
            shield.contentView=InputShield()
            return shield
        }
        skipPanel=panel(); skipControl=SkipControl(frame:NSRect(x:0,y:0,width:84,height:31))
        skipControl.onSkip=onSkip; skipControl.setAccessibilityRole(.button); skipControl.setAccessibilityLabel("跳过动画")
        skipPanel.contentView=skipControl
    }

    private var children: [PassivePanel] { shields+[skipPanel] }
    private func layoutChildren() {
        for (panel,frame) in zip(shields,OverlayLayout.shields(in:visual.frame)) { panel.setFrame(frame,display:false) }
        skipPanel.setFrame(OverlayLayout.skipFrame(in:visual.frame),display:false)
    }
    func apply(_ frame: NSRect) {
        let resized=visual.frame.size != frame.size
        if resized { visual.setFrame(frame,display:false) }
        else if visual.frame.origin != frame.origin { visual.setFrameOrigin(frame.origin) }
        if resized || !visible { layoutChildren() }
    }
    func show() {
        guard !visible else { return }
        layoutChildren(); visual.orderFrontRegardless()
        for child in children { visual.addChildWindow(child,ordered:.above) }
        visible=true
    }
    func hide() {
        guard visible else { return }
        for child in children { child.orderOut(nil) }
        visual.orderOut(nil); visible=false
    }
    deinit { hide() }
}
