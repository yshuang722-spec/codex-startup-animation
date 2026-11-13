import Foundation

struct Timing {
    static let upstreamDuration = 15.28
    let duration: Double
    var introEnd: Double { 5 * duration / Self.upstreamDuration }
    var sceneEnd: Double { 14.95 * duration / Self.upstreamDuration }
    var handoffDuration: Double { 0.33 * duration / Self.upstreamDuration }
    var timeout: Double { max(20, duration + 10) }
    init(duration: Double = Self.upstreamDuration) throws {
        guard duration.isFinite, (2...60).contains(duration) else { throw OptionsError.invalid("duration must be 2–60 seconds") }
        self.duration = duration
    }
}

enum OptionsError: Error, CustomStringConvertible {
    case invalid(String)
    var description: String { if case .invalid(let s) = self { return s }; return "Invalid options" }
}

struct Options {
    var preview = false
    var diagnostics = false
    var selfTest = false
    var renderDirectory: String?
    var clientBundleID = "com.openai.codex"
    var timing = try! Timing()
    var help = false
    init(_ arguments: [String]) throws {
        var i = 0
        while i < arguments.count {
            let argument = arguments[i]
            func value() throws -> String {
                guard i + 1 < arguments.count else { throw OptionsError.invalid("Missing value for \(argument)") }
                i += 1; return arguments[i]
            }
            switch argument {
            case "--preview": preview = true
            case "--diagnostics": diagnostics = true
            case "--self-test": selfTest = true
            case "--render-tests": renderDirectory = try value()
            case "--client-bundle-id":
                clientBundleID = try value()
                guard clientBundleID.contains("."), !clientBundleID.contains("/") else { throw OptionsError.invalid("Invalid bundle identifier") }
            case "--duration":
                guard let seconds = Double(try value()) else { throw OptionsError.invalid("Invalid duration") }
                timing = try Timing(duration: seconds)
            case "--help", "-h": help = true
            default: throw OptionsError.invalid("Unknown argument: \(argument)")
            }
            i += 1
        }
        guard [preview, diagnostics, selfTest, renderDirectory != nil].filter({ $0 }).count <= 1 else {
            throw OptionsError.invalid("Choose only one mode")
        }
    }
}

enum SessionAction: Equatable { case wait, openClient, beginHandoff, finish, timeout }

// Pure policy: time is supplied by the host, so this can be checked without opening windows.
struct SessionPolicy {
    let timing: Timing
    let preview: Bool
    private(set) var opened = false
    private(set) var handoffAt: Double?
    private(set) var finished = false
    mutating func advance(elapsed: Double, clientReady: Bool) -> SessionAction {
        guard !finished else { return .wait }
        if let handoffAt, elapsed >= handoffAt + timing.handoffDuration + 0.5 {
            finished = true; return .finish
        }
        if handoffAt != nil { return .wait }
        if elapsed >= timing.timeout { finished = true; return .timeout }
        if !preview, !opened, elapsed >= timing.introEnd { opened = true; return .openClient }
        if elapsed >= timing.sceneEnd, (preview || clientReady) {
            handoffAt = elapsed; return .beginHandoff
        }
        return .wait
    }
}

func runPolicyTests() throws -> [String] {
    var passed = [String]()
    func check(_ value: @autoclosure () -> Bool, _ name: String) throws {
        guard value() else { throw OptionsError.invalid("FAIL: \(name)") }; passed.append(name)
    }
    let timing = try Timing()
    var normal = SessionPolicy(timing: timing, preview: false)
    try check(normal.advance(elapsed: 0, clientReady: false) == .wait, "no early client launch")
    try check(normal.advance(elapsed: 5, clientReady: false) == .openClient, "launch once after intro")
    try check(normal.advance(elapsed: 5.1, clientReady: false) == .wait, "no duplicate launch")
    try check(normal.advance(elapsed: 15, clientReady: false) == .wait, "wait for launch callback before reveal")
    try check(normal.advance(elapsed: 16, clientReady: true) == .beginHandoff, "reveal when both conditions hold")
    try check(normal.advance(elapsed: 17, clientReady: true) == .finish, "host fallback exits after transition")
    try check(normal.advance(elapsed: 30, clientReady: true) == .wait, "no activity after completion")
    var preview = SessionPolicy(timing: timing, preview: true)
    try check(preview.advance(elapsed: 5, clientReady: false) == .wait, "preview never opens client")
    try check(preview.advance(elapsed: 14.95, clientReady: false) == .beginHandoff, "preview completes without client")
    var missing = SessionPolicy(timing: timing, preview: false)
    _ = missing.advance(elapsed: 5, clientReady: false)
    try check(missing.advance(elapsed: timing.timeout, clientReady: false) == .timeout, "missing client cannot leave overlay forever")
    let fast = try Timing(duration: 4)
    try check(abs(fast.sceneEnd + fast.handoffDuration - 4) < 0.000001, "duration scales complete sequence")
    for bad in ["nan", "inf", "1", "61", "oops"] {
        do { _ = try Options(["--duration", bad]); throw OptionsError.invalid("accepted bad duration") }
        catch OptionsError.invalid(let error) where error != "accepted bad duration" { passed.append("reject invalid duration \(bad)") }
    }
    for arguments in [["--duration"], ["--unknown"], ["--preview", "--diagnostics"], ["--client-bundle-id", "/bad"]] {
        do { _ = try Options(arguments); throw OptionsError.invalid("accepted invalid options") }
        catch OptionsError.invalid(let error) where error != "accepted invalid options" { passed.append("reject invalid options \(arguments.joined(separator: " "))") }
    }
    return passed
}
