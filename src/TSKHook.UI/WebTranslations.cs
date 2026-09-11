using System.Collections.Concurrent;
using System.Text.Json;
using Il2CppInterop.Runtime;
using TKS.Popup.View;
using UnityEngine;
using Vuplex.WebView;

namespace TSKHook.UI;

/// <summary>Local text-node translations for help and gacha-detail popups. Announcements remain original.</summary>
public static class WebTranslations
{
    private sealed class Session
    {
        public IWebView View = null!;
        public int Revision = -1;
        public bool Installed;
        public bool Pending;
        public int Request;
        public float RequestedAt;
        public float NextRequest;
        public Il2CppSystem.Action<string>? Callback;
        public bool RequestLogged;
        public bool CallbackLogged;
        public bool TimeoutLogged;
        public string? LastDiagnostic;
    }

    private readonly record struct Reply(Session Session, int Request, int Revision, string Json);
    private sealed record CaptureResult(string[] Sources, JsonElement Diagnostic);
    private static readonly Dictionary<IntPtr, Session> Sessions = new();
    private static readonly ConcurrentQueue<Reply> Replies = new();
    private static Dictionary<string, string> dictionary = new(StringComparer.Ordinal);
    private static readonly SortedDictionary<string, string> Missing = new(StringComparer.Ordinal);
    private static string dictionaryJson = "{}";
    private static string? missingPath;
    private static Action<string, string>? captureWriter;
    private static int revision;
    private static OnlineTranslation? online;
    private static float nextScan;
    private static bool wasEnabled;
    private static bool missingChanged;
    private static string? discoverySummary;

    /// <summary>Load the dictionary separately from the runtime capture output.</summary>
    public static void Reload(string path, string? capturePath = null, Action<string, string>? writeCapture = null)
    {
        using var file = File.OpenRead(path);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(file)
            ?? throw new InvalidDataException("The game WebView translation file must be a JSON object.");
        dictionary = loaded.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) &&
                                          !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        dictionaryJson = JsonSerializer.Serialize(dictionary);
        var outputPath = capturePath ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "web-missing.json");
        captureWriter = writeCapture;
        if (missingPath != outputPath)
        {
            Missing.Clear();
            if (File.Exists(outputPath))
            {
                using var previous = File.OpenRead(outputPath);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(previous);
                if (entries != null)
                    foreach (var entry in entries) Missing[entry.Key] = "";
            }
            missingPath = outputPath;
            missingChanged = true;
        }
        foreach (var key in Missing.Keys.Where(HasTranslation).ToArray())
        {
            Missing.Remove(key);
            missingChanged = true;
        }
        revision++;
        foreach (var session in Sessions.Values) session.NextRequest = 0;
        Plugin.Info($"Game WebView dictionary loaded: {dictionary.Count} entries.");
    }

    /// <summary>Request a fresh capture on the next Tick, without navigating the page.</summary>
    public static void Capture()
    {
        foreach (var session in Sessions.Values) session.NextRequest = 0;
    }

    /// <summary>Call from Update; active help/gacha-detail discovery and DOM scans run every two seconds.</summary>
    public static void Tick(bool enabled)
    {
        DrainReplies(enabled);
        var now = Time.unscaledTime;
        if (now >= nextScan)
        {
            nextScan = now + 2f;
            DiscoverGamePopups();
        }
        if (enabled != wasEnabled)
        {
            wasEnabled = enabled;
            foreach (var session in Sessions.Values) session.NextRequest = 0;
        }
        foreach (var session in Sessions.Values.ToArray())
        {
            if (session.Pending && now - session.RequestedAt < 5f) continue;
            if (session.Pending && !session.TimeoutLogged)
            {
                session.TimeoutLogged = true;
                Plugin.Info("Game WebView JavaScript callback has not arrived after 5 seconds; retrying.");
            }
            if (now < session.NextRequest) continue;
            Send(session, enabled, now);
        }
        if (missingChanged && missingPath != null)
        {
            var contents = JsonSerializer.Serialize(Missing, new JsonSerializerOptions { WriteIndented = true });
            if (captureWriter != null) captureWriter("web-missing.json", contents);
            else File.WriteAllText(missingPath, contents);
            missingChanged = false;
        }
    }

    private static void DiscoverGamePopups()
    {
        var active = new HashSet<IntPtr>();
        var helpObjects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<PopupHelpView>());
        var gachaDetailObjects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<PopupGachaDetailView>());
        var activePopups = 0;
        var controllers = 0;
        var prefabs = 0;
        var readyViews = 0;
        foreach (var instance in helpObjects.Concat(gachaDetailObjects))
        {
            try
            {
                var popup = instance.Cast<PopupViewBase>();
                if (!popup.gameObject.activeInHierarchy) continue;
                activePopups++;
                var helpPopup = instance.TryCast<PopupHelpView>();
                var controller = helpPopup != null ? helpPopup.controller
                    : instance.Cast<PopupGachaDetailView>().webView;
                if (controller == null) continue;
                controllers++;
                var prefab = controller.exeWebViewObject;
                if (prefab == null) continue;
                prefabs++;
                var view = prefab.WebView;
                if (view == null || view.IsDisposed || !view.IsInitialized) continue;
                readyViews++;
                active.Add(view.Pointer);
                if (!Sessions.ContainsKey(view.Pointer))
                    Sessions.Add(view.Pointer, new Session { View = view });
            }
            catch (Exception error) { Plugin.Error(error); }
        }
        var summary = $"Game WebView discovery: helpInstances={helpObjects.Length}, gachaDetailInstances={gachaDetailObjects.Length}, active={activePopups}, controllers={controllers}, prefabs={prefabs}, readyWebViews={readyViews}.";
        if (summary != discoverySummary)
        {
            discoverySummary = summary;
            Plugin.Info(summary);
        }
        foreach (var pointer in Sessions.Keys.Where(pointer => !active.Contains(pointer)).ToArray())
            Sessions.Remove(pointer);
    }

    private static void Send(Session session, bool enabled, float now)
    {
        // Popup teardown can dispose the view between the two-second discovery
        // scans. Capture/reload/toggle requests must not reuse that cached view.
        if (session.View.IsDisposed || !session.View.IsInitialized)
        {
            Sessions.Remove(session.View.Pointer);
            session.Pending = false;
            session.Callback = null;
            return;
        }
        var install = !session.Installed || session.Revision != revision;
        var options = install
            ? "{\"dictionary\":" + dictionaryJson + ",\"enabled\":" + (enabled ? "true" : "false") + ",\"capture\":true}"
            : "{\"enabled\":" + (enabled ? "true" : "false") + ",\"capture\":true}";
        var script = (install ? InstallerScript : "") +
            "\n(function () { if (!window.__tskWebTranslations) return 'null'; " +
            "const sources = window.__tskWebTranslations.configure(" + options + "); " +
            "return JSON.stringify({Sources: sources, Diagnostic: window.__tskWebTranslations.diagnose()}); })()";
        session.Pending = true;
        session.RequestedAt = now;
        session.NextRequest = now + 2f;
        var request = ++session.Request;
        var sentRevision = revision;
        session.Callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<string>>(
            new Action<string>(json => Replies.Enqueue(new Reply(session, request, sentRevision, json))));
        try
        {
            session.View.ExecuteJavaScript(script, session.Callback);
            if (!session.RequestLogged)
            {
                session.RequestLogged = true;
                Plugin.Info("Game WebView JavaScript capture requested.");
            }
        }
        catch (Exception error)
        {
            session.Pending = false;
            session.Installed = false;
            Plugin.Error(error);
        }
    }

    private static void DrainReplies(bool enabled)
    {
        while (Replies.TryDequeue(out var reply))
        {
            var session = reply.Session;
            if (!Sessions.TryGetValue(session.View.Pointer, out var current) ||
                !ReferenceEquals(current, session) || session.Request != reply.Request) continue;
            session.Pending = false;
            session.Callback = null;
            try
            {
                var capture = JsonSerializer.Deserialize<CaptureResult>(reply.Json);
                if (capture == null)
                {
                    // Navigation replaced the document and its injected helper.
                    session.Installed = false;
                    session.NextRequest = 0;
                    continue;
                }
                var sources = capture.Sources;
                if (!session.CallbackLogged)
                {
                    session.CallbackLogged = true;
                    Plugin.Info($"Game WebView JavaScript callback: missingNodes={sources.Length}.");
                }
                if (capture.Diagnostic.ValueKind == JsonValueKind.Object)
                {
                    var diagnostic = capture.Diagnostic.GetRawText();
                    if (diagnostic != session.LastDiagnostic)
                    {
                        session.LastDiagnostic = diagnostic;
                        Plugin.Info("Game WebView DOM diagnostic: " + diagnostic);
                    }
                }
                session.Installed = true;
                session.Revision = reply.Revision;
                if (session.Revision != revision) session.NextRequest = 0;
                foreach (var source in sources)
                {
                    if (string.IsNullOrWhiteSpace(source) || HasTranslation(source)) continue;
                    if (!Missing.ContainsKey(source)) { Missing.Add(source, ""); missingChanged = true; }
                    if (enabled) online?.Queue(source, "GameWebView/VisibleText");
                }
                RefreshOnlineCache();
            }
            catch (Exception error) { Plugin.Error(error); }
        }
    }

    internal static void ConfigureOnline(OnlineTranslation client)
    {
        online = client;
        RefreshOnlineCache();
    }

    internal static void RefreshOnlineCache()
    {
        if (online == null) return;
        var changed = false;
        foreach (var source in Missing.Keys.ToArray())
            if (!HasTranslation(source) && online.TryGet(source, out var translated))
            {
                dictionary[source] = translated;
                changed = true;
            }
        if (!changed) return;
        dictionaryJson = JsonSerializer.Serialize(dictionary);
        revision++;
        foreach (var session in Sessions.Values) session.NextRequest = 0;
    }

    private static bool HasTranslation(string source)
        => dictionary.ContainsKey(source) || dictionary.ContainsKey(source.Trim());

    internal const string InstallerScript = @"
(function () {
    if (window.__tskWebTranslations) return;
    const states = new Map();
    let dictionary = Object.create(null);
    let enabled = true;
    const own = (object, key) => Object.prototype.hasOwnProperty.call(object, key);

    function translation(original) {
        const key = own(dictionary, original) ? original : original.trim();
        const value = own(dictionary, key) ? dictionary[key] : null;
        return typeof value === 'string' && value.trim() ? value : null;
    }

    function elementVisible(element) {
        if (!element || element.closest('[hidden]') || !element.getClientRects().length) return false;
        const view = element.ownerDocument.defaultView;
        if (!view) return false;
        const style = view.getComputedStyle(element);
        return style.display !== 'none' && style.visibility !== 'hidden' && style.visibility !== 'collapse';
    }

    function visible(node) {
        const element = node.parentElement;
        if (!element || element.isContentEditable) return false;
        if (element.closest('script,style,noscript,template,input,textarea,select,option,iframe')) return false;
        return elementVisible(element);
    }

    function embeddedDocument(element) {
        try {
            if (element.contentDocument) return element.contentDocument;
        } catch (_) { return null; }
        // Chromium exposes same-origin HTML embed documents through their child
        // Window, while HTMLEmbedElement has no contentDocument property.
        const frames = element.ownerDocument.defaultView.frames;
        for (let i = 0; i < frames.length; i++) {
            try {
                if (frames[i].frameElement === element) return frames[i].document;
            } catch (_) { /* The browser keeps other origins unreadable. */ }
        }
        return null;
    }

    function documents() {
        const result = { pages: [document], iframeCount: 0, accessibleFrameCount: 0 };
        function visit(page) {
            for (const frame of page.querySelectorAll('iframe,embed,object')) {
                const isIframe = frame.tagName.toLowerCase() === 'iframe';
                if (isIframe) result.iframeCount++;
                if (!elementVisible(frame)) continue;
                const child = embeddedDocument(frame);
                // Cross-origin and sandboxed opaque frames expose no readable document.
                if (!child) continue;
                if (isIframe) result.accessibleFrameCount++;
                result.pages.push(child);
                visit(child);
            }
        }
        visit(document);
        return result;
    }

    function attached(node) {
        if (!node.isConnected) return false;
        const page = node.ownerDocument;
        if (page === document) return true;
        const frame = page.defaultView && page.defaultView.frameElement;
        return !!frame && frame.isConnected && embeddedDocument(frame) === page;
    }

    function textNodes(page, root) {
        root = root || page.body;
        if (!root) return [];
        const result = [];
        const walker = page.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        let node;
        while ((node = walker.nextNode()))
            if (node.nodeValue.trim() && visible(node)) result.push(node);
        return result;
    }

    function sourcePath(value, page) {
        if (!value) return null;
        try {
            const url = new URL(value, page.baseURI);
            return url.protocol === 'http:' || url.protocol === 'https:' ? url.pathname : null;
        } catch (_) { return null; }
    }

    function diagnoseCarrier(element) {
        const page = element.ownerDocument;
        const tag = element.tagName.toLowerCase();
        const rect = element.getBoundingClientRect();
        let child = null;
        if (tag === 'frame' || tag === 'iframe' || tag === 'object' || tag === 'embed')
            child = embeddedDocument(element);
        return {
            tag,
            sourcePath: sourcePath(element.currentSrc || element.getAttribute('src') || element.getAttribute('data'), page),
            type: element.getAttribute('type'),
            x: Math.round(rect.x), y: Math.round(rect.y),
            width: Math.round(rect.width), height: Math.round(rect.height),
            readableDocument: !!child,
            documentVisibleTextNodeCount: child ? textNodes(child).length : 0
        };
    }

    function diagnoseDocument(page) {
        const carriers = Array.from(page.querySelectorAll('img,object,embed,canvas,svg,frame,iframe')).filter(elementVisible);
        const shadows = Array.from(page.querySelectorAll('*')).filter(element => element.shadowRoot);
        const counts = { img: 0, object: 0, embed: 0, canvas: 0, svg: 0, frame: 0, iframe: 0 };
        for (const element of carriers) counts[element.tagName.toLowerCase()]++;
        return {
            path: sourcePath(page.URL, page),
            readyState: page.readyState,
            bodyTag: page.body ? page.body.tagName.toLowerCase() : null,
            bodyChildTags: page.body ? Array.from(page.body.children).slice(0, 12).map(element => element.tagName.toLowerCase()) : [],
            totalBodyTextLength: page.body ? (page.body.textContent || '').length : 0,
            visibleTextNodeCount: textNodes(page).length,
            visibleCarriers: counts,
            carriers: carriers.filter(element => element.tagName.toLowerCase() !== 'img')
                .concat(carriers.filter(element => element.tagName.toLowerCase() === 'img').slice(0, 12)).map(diagnoseCarrier),
            openShadowRootCount: shadows.length,
            shadowVisibleTextNodeCount: shadows.reduce((total, element) => total + textNodes(page, element.shadowRoot).length, 0)
        };
    }

    function diagnose() {
        const frames = documents();
        return {
            bodyTextLength: textNodes(document).reduce((total, node) => total + node.nodeValue.length, 0),
            iframeCount: frames.iframeCount,
            accessibleFrameCount: frames.accessibleFrameCount,
            visibleTextNodeCount: frames.pages.reduce((total, page) => total + textNodes(page).length, 0),
            documents: frames.pages.map(diagnoseDocument)
        };
    }

    function observe(node, state) {
        if (node.nodeValue !== state.applied) {
            state.original = node.nodeValue;
            state.applied = node.nodeValue;
        }
    }

    function configure(options) {
        options = options || {};
        const changedDictionary = own(options, 'dictionary');
        if (changedDictionary) dictionary = options.dictionary || Object.create(null);
        if (typeof options.enabled === 'boolean') enabled = options.enabled;

        for (const [node, state] of states) {
            if (!attached(node)) {
                states.delete(node);
                continue;
            }
            observe(node, state);
            if (!enabled || (changedDictionary && translation(state.original) === null)) {
                if (node.nodeValue !== state.original) node.nodeValue = state.original;
                state.applied = state.original;
            }
        }
        const missing = new Set();
        for (const page of documents().pages) {
          for (const node of textNodes(page)) {
            let state = states.get(node);
            if (!state) {
                state = { original: node.nodeValue, applied: node.nodeValue };
                states.set(node, state);
            } else {
                observe(node, state);
            }
            const value = translation(state.original);
            if (value === null && options.capture) missing.add(state.original);
            let desired = state.original;
            if (enabled && value !== null) {
                const leading = state.original.match(/^\s*/)[0];
                const trailing = state.original.match(/\s*$/)[0];
                desired = leading + value.trim() + trailing;
            }
            if (node.nodeValue !== desired) node.nodeValue = desired;
            state.applied = desired;
          }
        }
        return Array.from(missing);
    }
    window.__tskWebTranslations = { configure, diagnose };
})();
";
}
