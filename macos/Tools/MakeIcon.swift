import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

// Render the existing upstream vector geometry as a launcher icon. No app/window is created.
func parsePath(_ source: String) throws -> CGPath {
    let expression=try NSRegularExpression(pattern:"[A-Za-z]|[-+]?(?:[0-9]*\\.[0-9]+|[0-9]+\\.?[0-9]*)(?:[eE][-+]?[0-9]+)?")
    let ns=source as NSString
    let tokens=expression.matches(in:source,range:NSRange(location:0,length:ns.length)).map { ns.substring(with:$0.range) }
    let path=CGMutablePath(); var i=0; var command=""
    func number() throws -> CGFloat {
        guard i<tokens.count, let value=Double(tokens[i]), value.isFinite else { throw NSError(domain:"IconPath",code:1) }
        i+=1; return CGFloat(value)
    }
    while i<tokens.count {
        if tokens[i].first?.isLetter == true { command=tokens[i];i+=1 }
        switch command {
        case "M": path.move(to:CGPoint(x:try number(),y:try number())); command="L"
        case "L": path.addLine(to:CGPoint(x:try number(),y:try number()))
        case "H": path.addLine(to:CGPoint(x:try number(),y:path.currentPoint.y))
        case "V": path.addLine(to:CGPoint(x:path.currentPoint.x,y:try number()))
        case "C":
            let a=CGPoint(x:try number(),y:try number()), b=CGPoint(x:try number(),y:try number()), end=CGPoint(x:try number(),y:try number())
            path.addCurve(to:end,control1:a,control2:b)
        case "Z": path.closeSubpath(); command=""
        default: throw NSError(domain:"Unsupported SVG path command \(command)",code:2)
        }
    }
    return path
}

func color(_ r: CGFloat,_ g: CGFloat,_ b: CGFloat,_ a: CGFloat=1) -> CGColor {
    CGColor(colorSpace:CGColorSpace(name:CGColorSpace.sRGB)!,components:[r/255,g/255,b/255,a])!
}

func render(_ size: Int,_ logo: CGPath,_ output: URL) throws {
    let space=CGColorSpace(name:CGColorSpace.sRGB)!
    guard let context=CGContext(data:nil,width:size,height:size,bitsPerComponent:8,bytesPerRow:0,space:space,bitmapInfo:CGImageAlphaInfo.premultipliedLast.rawValue) else { throw NSError(domain:"IconContext",code:3) }
    context.scaleBy(x:CGFloat(size)/1024,y:CGFloat(size)/1024)
    context.setAllowsAntialiasing(true)
    let tile=CGPath(roundedRect:CGRect(x:64,y:64,width:896,height:896),cornerWidth:198,cornerHeight:198,transform:nil)
    context.saveGState(); context.addPath(tile);context.clip()
    let background=CGGradient(colorsSpace:space,colors:[color(15,49,42),color(3,17,19)] as CFArray,locations:[0,1])!
    context.drawLinearGradient(background,start:CGPoint(x:80,y:960),end:CGPoint(x:850,y:64),options:[])
    context.restoreGState()
    context.addPath(tile);context.setStrokeColor(color(74,164,130,0.45));context.setLineWidth(3);context.strokePath()
    context.saveGState()
    context.translateBy(x:40,y:1010);context.scaleBy(x:944/721,y:-944/721)
    context.addPath(logo);context.clip()
    let tint=CGGradient(colorsSpace:space,colors:[color(0,211,238),color(87,255,169)] as CFArray,locations:[0,1])!
    context.drawLinearGradient(tint,start:CGPoint(x:120,y:140),end:CGPoint(x:600,y:610),options:[.drawsBeforeStartLocation,.drawsAfterEndLocation])
    context.restoreGState()
    // The play badge distinguishes this entry from the original client.
    let badge=CGRect(x:690,y:112,width:232,height:232)
    context.setFillColor(color(87,255,169));context.fillEllipse(in:badge)
    context.setStrokeColor(color(3,22,18));context.setLineWidth(17);context.strokeEllipse(in:badge)
    context.beginPath();context.move(to:CGPoint(x:777,y:168));context.addLine(to:CGPoint(x:777,y:288));context.addLine(to:CGPoint(x:873,y:228));context.closePath()
    context.setFillColor(color(3,30,23));context.fillPath()
    guard let image=context.makeImage(), let destination=CGImageDestinationCreateWithURL(output as CFURL,UTType.png.identifier as CFString,1,nil) else { throw NSError(domain:"IconPNG",code:4) }
    CGImageDestinationAddImage(destination,image,nil)
    guard CGImageDestinationFinalize(destination) else { throw NSError(domain:"IconWrite",code:5) }
}

guard CommandLine.arguments.count==4 else { fputs("Usage: MakeIcon.swift LOGO_PATH ICONSET_DIRECTORY PREVIEW_DIRECTORY\n",stderr);exit(2) }
do {
    let logo=try parsePath(String(contentsOfFile:CommandLine.arguments[1],encoding:.utf8))
    let set=URL(fileURLWithPath:CommandLine.arguments[2]), preview=URL(fileURLWithPath:CommandLine.arguments[3])
    try FileManager.default.createDirectory(at:set,withIntermediateDirectories:true)
    try FileManager.default.createDirectory(at:preview,withIntermediateDirectories:true)
    for (name,size) in [("icon_16x16",16),("icon_16x16@2x",32),("icon_32x32",32),("icon_32x32@2x",64),("icon_128x128",128),("icon_128x128@2x",256),("icon_256x256",256),("icon_256x256@2x",512),("icon_512x512",512),("icon_512x512@2x",1024)] {
        try render(size,logo,set.appendingPathComponent(name+".png"))
    }
    try render(256,logo,preview.appendingPathComponent("launcher-icon.png"))
    try render(96,logo,preview.appendingPathComponent("launcher-icon-small.png"))
    print("Rendered launcher icon sizes 16–1024")
} catch { fputs("Icon generation failed: \(error)\n",stderr);exit(1) }
