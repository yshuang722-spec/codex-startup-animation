import AppKit
import WebKit
import Darwin

func report(_ message: String) { fputs("[CodexStartupMac] \(message)\n", stderr) }
func printJSON(_ value: Any) {
    if let data = try? JSONSerialization.data(withJSONObject: value, options: [.prettyPrinted, .sortedKeys]),
       let text = String(data: data, encoding: .utf8) { print(text) }
}

final class AnimationWebView: WKWebView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

final class Host: NSObject, NSApplicationDelegate, WKNavigationDelegate, WKScriptMessageHandler {
    let options: Options
    var policy: SessionPolicy
    var web: WKWebView!
    var presenter: OverlayPresenter?
    var pointerMonitor: Any?
    var pointerTracking: PointerTracking?
    let geometryQueue=DispatchQueue(label:"codex-startup.window-metadata",qos:.userInteractive)
    var geometryPending=false
    var geometryEpoch=0
    var pointerUpdateCount=0
    var pointerHandlerMaxMS=0.0
    var ticker: Timer?
    var watchdog: Timer?
    var startedAt: TimeInterval?
    var client: NSRunningApplication?
    var previousApp: NSRunningApplication?
    var openInFlight = false
    var clientReady = false
    var completed = false
    var skipped = false
    var lockFD: Int32 = -1
    var targetWindow: ClientWindow?
    var nextWindowProbe: TimeInterval = 0
    var exitCode: Int32 = 0
    var renderResults = [[String: Any]]()
    let resourceURL: URL

    init(options: Options, resourceURL: URL) {
        self.options = options
        self.policy = SessionPolicy(timing: options.timing, preview: options.preview)
        self.resourceURL = resourceURL
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let rendering = options.renderDirectory != nil
        if !rendering {
            let lockURL = FileManager.default.temporaryDirectory.appendingPathComponent("codex-startup-mac-\(getuid()).lock")
            lockFD = Darwin.open(lockURL.path, O_CREAT | O_RDWR, S_IRUSR | S_IWUSR)
            guard lockFD >= 0, flock(lockFD, LOCK_EX | LOCK_NB) == 0 else {
                report("Another startup animation is active; ignoring duplicate launch")
                NSApp.terminate(nil); return
            }
        }
        let controller = WKUserContentController()
        controller.add(self, name: "boot")
        let configuration = WKWebViewConfiguration()
        configuration.userContentController = controller
        configuration.websiteDataStore = .nonPersistent()
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = false
        // This placeholder is never shown: runtime must locate a real Codex window first.
        let frame = rendering ? NSRect(x: 0, y: 0, width: 1440, height: 900) : NSRect(x: 0, y: 0, width: 1, height: 1)
        web = AnimationWebView(frame: NSRect(origin: .zero, size: frame.size), configuration: configuration)
        web.autoresizingMask = [.width, .height]
        web.navigationDelegate = self
        web.setValue(false, forKey: "drawsBackground")
        if !rendering {
            previousApp = NSWorkspace.shared.frontmostApplication
            presenter=OverlayPresenter(web:web,onSkip:{ [weak self] in self?.skip() })
        }
        watchdog = Timer.scheduledTimer(withTimeInterval: rendering ? 45 : 10, repeats: false) { [weak self] _ in
            self?.fail("Local animation page did not become ready")
        }
        web.loadFileURL(resourceURL.appendingPathComponent("index.html"), allowingReadAccessTo: resourceURL)
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url, url.isFileURL,
              url.standardizedFileURL.path.hasPrefix(resourceURL.standardizedFileURL.path + "/") else {
            decisionHandler(.cancel); return
        }
        decisionHandler(.allow)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { fail(error.localizedDescription) }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { fail(error.localizedDescription) }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) { fail("Animation renderer exited unexpectedly") }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard !completed else { return }
        watchdog?.invalidate()
        if options.renderDirectory != nil { renderTests(); return }
        if !options.preview { openClient() }
        else { client = NSRunningApplication.runningApplications(withBundleIdentifier: options.clientBundleID).first(where: { !$0.isTerminated }) }
        guard !completed else { return }
        watchdog = Timer.scheduledTimer(withTimeInterval: 20, repeats: false) { [weak self] _ in
            self?.fail("No visible Codex window found; no animation was shown")
        }
        ticker = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in self?.prepareWindow() }
        prepareWindow()
    }

    func prepareWindow() {
        guard !completed, startedAt == nil, let client,
              let window = WindowLocator.find(pid: client.processIdentifier) else { return }
        targetWindow = window
        presenter?.apply(OverlayLayout.visualFrame(in:window.appKitFrame(primaryTop:WindowLocator.primaryTop)))
        ticker?.invalidate(); watchdog?.invalidate()
        _ = client.activate(options: [.activateIgnoringOtherApps])
        let reduced = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        // Resizing the host view precedes the canvas start, so the first visible frame is window-sized.
        web.evaluateJavaScript("document.documentElement.classList.add('native-controls'); window.boot.start(\(options.timing.duration), \(reduced ? "true" : "false"))") { [weak self] _, error in
            guard let self, !self.completed else { return }
            if let error { self.fail(error.localizedDescription); return }
            self.startedAt = ProcessInfo.processInfo.systemUptime
            self.presenter?.show()
            self.pointerMonitor=NSEvent.addGlobalMonitorForEvents(matching:[.leftMouseDown,.leftMouseDragged,.leftMouseUp]) { [weak self] event in
                self?.handlePointer(event)
            }
            guard self.pointerMonitor != nil else { self.fail("Mouse drag observation is unavailable; removing animation"); return }
            self.ticker = Timer.scheduledTimer(withTimeInterval: 0.05, repeats: true) { [weak self] _ in self?.tick() }
            report("Animation started; preview=\(self.options.preview), reducedMotion=\(reduced)")
        }
    }

    func tick() {
        guard !completed, !skipped, let startedAt else { return }
        let now = ProcessInfo.processInfo.systemUptime
        if let client {
            let foreground = NSWorkspace.shared.frontmostApplication?.processIdentifier
            let shouldBeVisible = foreground == getpid() || foreground == client.processIdentifier
            if shouldBeVisible { presenter?.show() }
            else if presenter?.visible == true { presenter?.hide(); pointerTracking=nil; geometryEpoch += 1 }
        }
        if now >= nextWindowProbe {
            nextWindowProbe=now+0.25
            requestGeometry()
        }
        let action = policy.advance(elapsed: ProcessInfo.processInfo.systemUptime - startedAt, clientReady: clientReady)
        if client?.isTerminated == true { fail("Client exited during animation"); return }
        switch action {
        case .wait: break
        case .openClient: openClient()
        case .beginHandoff:
            report("Beginning handoff")
            web.evaluateJavaScript("window.boot.handoff()") { [weak self] _, error in
                if let error { self?.fail(error.localizedDescription) }
            }
        case .finish: finish()
        case .timeout: fail("Client launch timed out; removing animation")
        }
    }

    func requestGeometry() {
        guard !completed, pointerTracking == nil, !geometryPending, let client, let targetWindow else { return }
        geometryPending=true
        let pid=client.processIdentifier, id=targetWindow.id, epoch=geometryEpoch
        geometryQueue.async { [weak self] in
            let updated=WindowLocator.find(pid:pid,preferredID:id)
            DispatchQueue.main.async {
                guard let self else { return }
                self.geometryPending=false
                guard !self.completed, self.pointerTracking == nil else { return }
                guard epoch == self.geometryEpoch else { self.requestGeometry(); return }
                guard let updated else {
                    report("Target window no longer visible; dismissing animation")
                    self.finish(activateClient:false); return
                }
                self.targetWindow=updated
                self.presenter?.apply(updated.appKitFrame(primaryTop:WindowLocator.primaryTop))
            }
        }
    }

    func handlePointer(_ event: NSEvent) {
        guard !completed, !skipped, let client, let presenter else { return }
        let begin=ProcessInfo.processInfo.systemUptime
        let point=NSEvent.mouseLocation
        switch event.type {
        case .leftMouseDown:
            guard presenter.visible, NSWorkspace.shared.frontmostApplication?.processIdentifier == client.processIdentifier else { return }
            pointerTracking=PointerTracking.begin(at:point,frame:presenter.frame)
            if pointerTracking != nil { geometryEpoch += 1 }
        case .leftMouseDragged:
            guard let tracking=pointerTracking else { return }
            // No WindowServer query and no timer wait in this path. Translation never resizes WebKit.
            presenter.apply(tracking.frame(at:point)); pointerUpdateCount += 1
        case .leftMouseUp:
            guard let tracking=pointerTracking else { return }
            presenter.apply(tracking.frame(at:point))
            pointerTracking=nil; geometryEpoch += 1; requestGeometry()
        default: break
        }
        pointerHandlerMaxMS=max(pointerHandlerMaxMS,(ProcessInfo.processInfo.systemUptime-begin)*1000)
    }

    func openClient() {
        guard !options.preview, !completed else { return }
        if let running = NSRunningApplication.runningApplications(withBundleIdentifier: options.clientBundleID).first(where: { !$0.isTerminated }) {
            client = running
            clientReady = running.isFinishedLaunching
            if startedAt == nil && !options.preview {
                _ = running.activate(options: [.activateIgnoringOtherApps])
            }
            if skipped { finish() }
            if !clientReady { pollClientLaunch() }
            return
        }
        guard !openInFlight else { return }
        guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: options.clientBundleID) else {
            fail("Client not found: \(options.clientBundleID)"); return
        }
        openInFlight = true
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = false
        configuration.addsToRecentItems = false
        NSWorkspace.shared.openApplication(at: url, configuration: configuration) { [weak self] application, error in
            DispatchQueue.main.async {
                guard let self, !self.completed else { return }
                self.openInFlight = false
                if let error { self.fail(error.localizedDescription); return }
                guard let application else { self.fail("Client did not return a running application"); return }
                self.client = application
                self.clientReady = application.isFinishedLaunching
                report("Client launch returned pid=\(application.processIdentifier)")
                if self.skipped { self.finish() }
                else if !self.clientReady { self.pollClientLaunch() }
            }
        }
    }

    func pollClientLaunch() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { [weak self] in
            guard let self, !self.completed, let client = self.client else { return }
            if client.isTerminated { self.fail("Client exited during launch"); return }
            self.clientReady = client.isFinishedLaunching
            if !self.clientReady { self.pollClientLaunch() }
        }
    }

    func skip() {
        guard !completed, !skipped else { return }
        report("Animation skipped")
        skipped = true
        presenter?.hide()
        if options.preview || clientReady { finish(); return }
        openClient()
        // The renderer may still be running, but the overlay is gone and this host is bounded.
        watchdog?.invalidate()
        watchdog = Timer.scheduledTimer(withTimeInterval: 15, repeats: false) { [weak self] _ in self?.fail("Client launch after skip timed out") }
    }

    func finish(activateClient: Bool = true) {
        guard !completed else { return }
        completed = true
        ticker?.invalidate(); watchdog?.invalidate()
        stopPointerMonitor()
        presenter?.hide()
        // Switching to another app during playback must not cause a focus steal at the end.
        let foreground=NSWorkspace.shared.frontmostApplication?.processIdentifier
        let relevantFocus=foreground == getpid() || foreground == client?.processIdentifier
        if relevantFocus && activateClient {
            if options.preview { _ = previousApp?.activate(options: [.activateIgnoringOtherApps]) }
            else { _ = client?.activate(options: [.activateIgnoringOtherApps]) }
        }
        report("Animation host exiting; pointerUpdates=\(pointerUpdateCount), maxHandlerMS=\(String(format:"%.3f",pointerHandlerMaxMS))")
        NSApp.terminate(nil)
    }

    func fail(_ message: String) {
        guard !completed else { return }
        report("ERROR: \(message)")
        exitCode = 1
        if options.renderDirectory != nil {
            completed = true; watchdog?.invalidate(); NSApp.terminate(nil)
        } else { finish() }
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "boot", message.frameInfo.isMainFrame, let value = message.body as? String else { return }
        if value == "finished", policy.handoffAt != nil { finish() }
        else if value == "skip", options.renderDirectory == nil { skip() }
        else if value.hasPrefix("error:") {
            if options.renderDirectory != nil { report(value) } else { fail(value) }
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { false }
    func stopPointerMonitor() {
        if let pointerMonitor { NSEvent.removeMonitor(pointerMonitor); self.pointerMonitor=nil }
        pointerTracking=nil; geometryEpoch += 1
    }
    func applicationWillTerminate(_ notification: Notification) {
        ticker?.invalidate(); watchdog?.invalidate()
        stopPointerMonitor(); presenter?.hide()
        web?.configuration.userContentController.removeScriptMessageHandler(forName: "boot")
        if lockFD >= 0 { _ = flock(lockFD, LOCK_UN); Darwin.close(lockFD) }
        fflush(stdout); fflush(stderr)
        Darwin.exit(exitCode)
    }

    func renderTests() {
        let cases: [(String, Int, Int, Double, Bool)] = [
            ("01-intro",1440,900,2.5,false), ("02-code",1440,900,5.8,false),
            ("03-cards",1440,900,8.7,false), ("04-ready",1440,900,12.2,false),
            ("05-crt-line",1440,900,15.05,false), ("06-crt-reveal",1440,900,15.20,false),
            ("07-finished",1440,900,15.28,false), ("08-narrow-cards",900,650,8.7,false),
            ("09-narrow-ready",900,650,12.2,false), ("10-small-ready",390,844,12.2,false),
            ("11-reduced-intro",1440,900,2.5,true), ("12-reduced-handoff",1440,900,15.1,true)
        ]
        guard let destination = options.renderDirectory else { return }
        do { try FileManager.default.createDirectory(atPath: destination, withIntermediateDirectories: true) }
        catch { fail(error.localizedDescription); return }
        watchdog = Timer.scheduledTimer(withTimeInterval: 45, repeats: false) { [weak self] _ in self?.fail("Offscreen render tests timed out") }
        func next(_ index: Int) {
            if index == cases.count {
                self.web.evaluateJavaScript("(() => { try { return {passed:window.boot.selfTest()}; } catch (error) { return {failure:String(error), stack:error.stack}; } })()") { result, error in
                    if let error { self.fail(error.localizedDescription); return }
                    if let checks = result as? [String: Any], let failure = checks["failure"] as? String {
                        self.fail("Render assertion: \(failure); \(checks["stack"] ?? "")"); return
                    }
                    self.captureInterface(destination: destination, checks: result ?? NSNull())
                }
                return
            }
            let (name, width, height, time, reduced) = cases[index]
            self.web.evaluateJavaScript("window.boot.capture(\(width),\(height),\(time),\(reduced))") { result, error in
                if let error { self.fail("\(name): \(error as NSError)"); return }
                guard var result = result as? [String: Any], let image = result.removeValue(forKey: "png") as? String,
                      let encoded = image.split(separator: ",").last, let data = Data(base64Encoded: String(encoded)) else {
                    self.fail("Invalid render result: \(name)"); return
                }
                do { try data.write(to: URL(fileURLWithPath: destination).appendingPathComponent(name + ".png")) }
                catch { self.fail(error.localizedDescription); return }
                result["name"] = name
                self.renderResults.append(result)
                next(index + 1)
            }
        }
        next(0)
    }

    func captureInterface(destination: String, checks: Any) {
        web.setFrameSize(NSSize(width: 900, height: 650))
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
            self.web.evaluateJavaScript("document.documentElement.classList.add('native-controls'); window.boot.capture(900,650,12.2,false); document.getElementById('skip').getBoundingClientRect().toJSON()") { result, error in
                if let error { self.fail(error.localizedDescription); return }
                let configuration = WKSnapshotConfiguration()
                configuration.rect = NSRect(x: 0,y: 0,width: 900,height: 650)
                configuration.afterScreenUpdates = false
                self.web.takeSnapshot(with: configuration) { image, error in
                    if let error { self.fail(error.localizedDescription); return }
                    guard let image else { self.fail("Missing interface image"); return }
                    let composed=NSImage(size:image.size)
                    composed.lockFocus()
                    image.draw(in:NSRect(origin:.zero,size:image.size))
                    let control=SkipControl(frame:NSRect(x:0,y:0,width:84,height:31))
                    if let pixels=control.bitmapImageRepForCachingDisplay(in:control.bounds) {
                        control.cacheDisplay(in:control.bounds,to:pixels)
                        let controlImage=NSImage(size:control.bounds.size);controlImage.addRepresentation(pixels)
                        controlImage.draw(in:OverlayLayout.skipFrame(in:NSRect(origin:.zero,size:image.size)))
                    }
                    composed.unlockFocus()
                    guard let tiff = composed.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
                          let png = bitmap.representation(using: .png, properties: [:]) else { self.fail("Cannot export interface snapshot"); return }
                    let output: [String: Any] = ["engine":"macOS WKWebView (offscreen Canvas and DOM snapshot)", "rendered":self.renderResults,
                                               "checks":checks, "interfaceSnapshot":["file":"13-interface.png", "nativeSkipControl":true,"skipButton":result ?? NSNull()]]
                    do {
                        try png.write(to: URL(fileURLWithPath: destination).appendingPathComponent("13-interface.png"))
                        let data = try JSONSerialization.data(withJSONObject: output, options: [.prettyPrinted, .sortedKeys])
                        try data.write(to: URL(fileURLWithPath: destination).appendingPathComponent("render-results.json"))
                    } catch { self.fail(error.localizedDescription); return }
                    printJSON(output)
                    self.completed = true; self.watchdog?.invalidate(); NSApp.terminate(nil)
                }
            }
        }
    }
}

do {
    let options = try Options(Array(CommandLine.arguments.dropFirst()))
    if options.help {
        print("CodexStartupMac [--preview | --diagnostics | --self-test | --render-tests DIRECTORY] [--duration 2...60] [--client-bundle-id ID]")
        exit(0)
    }
    if options.selfTest { printJSON(["passed":try runPolicyTests(),"windowPassed":try runWindowTests(),"pointerPassed":try runPointerTests()]); exit(0) }
    let resourceURL = Bundle.main.resourceURL ?? URL(fileURLWithPath: CommandLine.arguments[0]).deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources")
    if options.diagnostics {
        let clientURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: options.clientBundleID)
        let running = NSRunningApplication.runningApplications(withBundleIdentifier: options.clientBundleID).first(where: { !$0.isTerminated })
        let window = running.flatMap { WindowLocator.find(pid: $0.processIdentifier) }
        let bounds: Any = window.map { ["x":$0.quartzBounds.minX, "y":$0.quartzBounds.minY, "width":$0.quartzBounds.width, "height":$0.quartzBounds.height] } ?? NSNull()
        printJSON(["clientBundleID": options.clientBundleID, "clientPath": clientURL?.path ?? NSNull(),
                   "launcherName":Bundle.main.object(forInfoDictionaryKey:"CFBundleDisplayName") ?? NSNull(),
                   "launcherVersion":Bundle.main.object(forInfoDictionaryKey:"CFBundleShortVersionString") ?? NSNull(),
                   "launcherPath":Bundle.main.bundleURL.path,
                   "resourcesPresent": FileManager.default.fileExists(atPath: resourceURL.appendingPathComponent("index.html").path),
                   "architecture": "arm64", "residentWatcher": false, "durationSeconds": options.timing.duration,
                   "animationScope":"completeClientWindow", "visibleClientWindow":window != nil,"clientWindowBounds":bounds,
                   "visualMargin":0,"dragTracking":"mouseEvents","geometryQueries":"background; suspended during pointer drag"])
        exit(clientURL == nil ? 1 : 0)
    }
    guard FileManager.default.fileExists(atPath: resourceURL.appendingPathComponent("index.html").path) else {
        throw OptionsError.invalid("Missing bundled animation resources; run build.sh")
    }
    let app = NSApplication.shared
    app.setActivationPolicy(options.renderDirectory == nil ? .regular : .prohibited)
    let host = Host(options: options, resourceURL: resourceURL)
    app.delegate = host
    app.run()
    exit(host.exitCode)
} catch {
    report(String(describing: error)); exit(2)
}
