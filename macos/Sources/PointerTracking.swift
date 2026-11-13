import Foundation

enum PointerOperation: Equatable {
    case move
    case resize(left: Bool, right: Bool, bottom: Bool, top: Bool)
}

struct PointerTracking {
    let operation: PointerOperation
    let origin: NSPoint
    let frame: NSRect

    static func begin(at point: NSPoint, frame: NSRect) -> PointerTracking? {
        let margin = OverlayLayout.resizeHitMargin
        guard frame.insetBy(dx:-6,dy:-6).contains(point) else { return nil }
        let left = abs(point.x-frame.minX) <= margin, right = abs(point.x-frame.maxX) <= margin
        let bottom = abs(point.y-frame.minY) <= margin, top = abs(point.y-frame.maxY) <= margin
        if left || right || bottom || top { return Self(operation:.resize(left:left,right:right,bottom:bottom,top:top),origin:point,frame:frame) }
        if point.y >= frame.maxY-OverlayLayout.dragHeight,
           point.x >= frame.minX+OverlayLayout.headerLeftGuard,
           point.x < frame.maxX-OverlayLayout.headerRightGuard {
            return Self(operation:.move,origin:point,frame:frame)
        }
        return nil
    }

    func frame(at point: NSPoint) -> NSRect {
        let dx = point.x-origin.x, dy = point.y-origin.y
        if operation == .move { return frame.offsetBy(dx:dx,dy:dy) }
        guard case let .resize(left,right,bottom,top) = operation else { return frame }
        var minX = frame.minX, maxX = frame.maxX, minY = frame.minY, maxY = frame.maxY
        if left { minX = min(maxX-320,minX+dx) }
        if right { maxX = max(minX+320,maxX+dx) }
        if bottom { minY = min(maxY-200,minY+dy) }
        if top { maxY = max(minY+200,maxY+dy) }
        return NSRect(x:minX,y:minY,width:maxX-minX,height:maxY-minY)
    }
}

func runPointerTests() throws -> [String] {
    var passed = [String]()
    func check(_ condition: @autoclosure () -> Bool,_ name: String) throws {
        guard condition() else { throw OptionsError.invalid("FAIL: \(name)") }; passed.append(name)
    }
    let frame = NSRect(x:200,y:100,width:1000,height:700)
    let drag = PointerTracking.begin(at:NSPoint(x:600,y:780),frame:frame)
    try check(drag?.operation == .move,"native center titlebar drag is identified")
    try check(drag?.frame(at:NSPoint(x:670,y:745)) == frame.offsetBy(dx:70,dy:-35),"pointer movement maps directly to visual movement without a polling interval")
    try check(PointerTracking.begin(at:NSPoint(x:600,y:400),frame:frame) == nil,"content clicks do not begin window dragging")
    try check(PointerTracking.begin(at:NSPoint(x:230,y:780),frame:frame) == nil,"hidden original header buttons are not drag targets")
    let resize = PointerTracking.begin(at:NSPoint(x:1198,y:102),frame:frame)
    try check(resize?.frame(at:NSPoint(x:1298,y:52)) == NSRect(x:200,y:50,width:1100,height:750),"native bottom-right resize preserves opposite corner")
    let left = PointerTracking.begin(at:NSPoint(x:202,y:400),frame:frame)
    try check(left?.frame(at:NSPoint(x:252,y:400)) == NSRect(x:250,y:100,width:950,height:700),"native left resize preserves right edge")
    try check(left?.frame(at:NSPoint(x:2000,y:400)).width == 320,"resize prediction has a bounded minimum before native reconciliation")
    let expected = (1...120).allSatisfy { i in
        drag?.frame(at:NSPoint(x:600+CGFloat(i),y:780-CGFloat(i))) == frame.offsetBy(dx:CGFloat(i),dy:-CGFloat(i))
    }
    try check(expected,"120 consecutive pointer updates have no accumulated coordinate lag")
    return passed
}
