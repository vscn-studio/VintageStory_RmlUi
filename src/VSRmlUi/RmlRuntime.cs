// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Text;

namespace VSRmlUi;

internal interface IRmlHost
{
    byte[] ReadAsset(string path);
    (byte[] Pixels, int Width, int Height) ReadImage(string path);
    string Translate(string input);
    string Clipboard { get; set; }
    string Cursor { set; }
    void Log(int level, string message);
}

internal sealed class RmlRuntime : IRmlUiService, IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly IRmlHost host;
    private readonly Native.Callbacks callbacks;
    private readonly Dictionary<ulong, RmlDocument> documents = [];
    private readonly Dictionary<ulong, Subscription> subscriptions = [];
    private readonly HashSet<string> fonts = [];
    private readonly Dictionary<object, RmlDocument> hosts = new(ReferenceEqualityComparer.Instance);
    private bool draining;
    private bool disposing;
    internal Func<RmlDocument, IDocumentView>? CreateView { get; set; }
    internal Func<(int Width, int Height, float Scale)> Dimensions { get; set; } = () => (1280, 720, 1);
    public bool IsAvailable { get; private set; }
    public string Version => "1.0.0";
    public string RmlUiVersion => "6.4-dev (3045e6e)";

    internal RmlRuntime(IRmlHost host, bool headless = false)
    {
        this.host = host;
        callbacks = new() { Read = Read, Free = Marshal.FreeHGlobal, Log = Log, Write = Write };
        if (Native.vr_abi() != 1) throw new RmlUiException("The managed and native RmlUi versions do not match.");
        if (Native.vr_init(in callbacks, headless ? 1 : 0) == 0) { Native.Check(); throw new RmlUiException("RmlUi initialization failed."); }
        IsAvailable = true;
    }
    internal void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("RmlUi must be used on the client thread. Use api.Event.EnqueueMainThreadTask for background results.");
        ObjectDisposedException.ThrowIf(!IsAvailable, this);
    }
    public RmlDocument LoadDocument(string ownerModId, string assetPath, RmlDocumentOptions? options = null) => Load(ownerModId, assetPath, null, options);
    public RmlDocument LoadDocumentFromString(string ownerModId, string markup, string sourcePath, RmlDocumentOptions? options = null)
    { ArgumentNullException.ThrowIfNull(markup); return Load(ownerModId, sourcePath, markup, options); }
    private RmlDocument Load(string owner, string path, string? markup, RmlDocumentOptions? options)
    {
        CheckThread();
        if (disposing) throw new ObjectDisposedException(nameof(RmlRuntime));
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!owner.All(char.IsAsciiLetterOrDigit)) throw new ArgumentException("Owner must be a Vintage Story mod ID.", nameof(owner));
        path = RmlAssetPath.Normalize(path);
        options ??= new();
        if (!Enum.IsDefined(options.Mode) || !double.IsFinite(options.DrawOrder)) throw new ArgumentException("Invalid document options.", nameof(options));
        var size = Dimensions();
        ulong handle = Native.vr_load(path, markup, size.Width, size.Height, size.Scale); Native.Check();
        if (handle == 0) throw new RmlUiException("Document creation failed.");
        var document = new RmlDocument(this, handle, owner, path, options);
        documents.Add(handle, document);
        try { document.UpdateViewport(); document.View = CreateView?.Invoke(document); }
        catch { document.Dispose(); throw; }
        return document;
    }
    public void RegisterFont(string assetPath, string family, int weight = 400, bool italic = false, bool fallback = false)
    {
        CheckThread(); ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (weight is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(weight));
        string path = RmlAssetPath.Normalize(assetPath);
        string key = $"{path}|{family}|{weight}|{italic}|{fallback}";
        if (fonts.Contains(key)) return;
        Native.vr_font(path, family, weight, italic ? 1 : 0, fallback ? 1 : 0); Native.Check(); fonts.Add(key);
    }
    public void CloseAll(string ownerModId) { CheckThread(); foreach (var d in documents.Values.Where(d => d.OwnerModId == ownerModId).ToArray()) if (!d.IsDisposed) d.Close(); }
    public void ReleaseAll(string ownerModId) { CheckThread(); foreach (var d in documents.Values.Where(d => d.OwnerModId == ownerModId).ToArray()) d.Dispose(); }
    public bool TryGetDocument(object host, out RmlDocument? document) { CheckThread(); ArgumentNullException.ThrowIfNull(host); return hosts.TryGetValue(host, out document) && !document.IsDisposed; }
    public void AttachHost(object host, RmlDocument document) { CheckThread(); ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(document); if (!documents.ContainsKey(document.Handle)) throw new InvalidOperationException("Document belongs to another runtime."); hosts[host] = document; }
    internal void Remove(RmlDocument document)
    {
        documents.Remove(document.Handle);
        foreach (var host in hosts.Where(x => ReferenceEquals(x.Value, document)).Select(x => x.Key).ToArray()) hosts.Remove(host);
        foreach (var item in subscriptions.Values.Where(s => s.Document == document).ToArray()) { subscriptions.Remove(item.Id); item.Detach(); }
    }
    internal IDisposable Listen(RmlElement element, string type, Action<RmlEvent> callback, bool capture)
    {
        ulong id = Native.vr_listen(element.Document.Handle, element.Handle, type, capture ? 1 : 0); Native.Check();
        var subscription = new Subscription(this, id, element.Document, callback); subscriptions.Add(id, subscription); return subscription;
    }
    internal void DrainEvents()
    {
        if (draining || !IsAvailable) return;
        CheckThread(); draining = true;
        try
        {
            // Bound recursive application events to avoid locking the game in one frame.
            for (int count = 0; count < 4096 && IsAvailable; count++)
            {
                int available = Native.vr_poll(out var ev); Native.Check(); if (available == 0) break;
                if (available == 2)
                {
                    if (subscriptions.Remove(ev.Subscription, out var removed)) removed.Detach();
                    continue;
                }
                string type = Native.Utf8(Native.vr_event_value(0)), value = Native.Utf8(Native.vr_event_value(1)), targetId = Native.Utf8(Native.vr_event_value(2));
                if (subscriptions.TryGetValue(ev.Subscription, out var sub) && documents.TryGetValue(ev.Document, out var document))
                {
                    var snapshot = new RmlEvent(type, value, targetId, new(document, ev.Target), document,
                        EventInt(3), EventInt(4), EventInt(5), EventFloat(6), EventInt(7), document.CapturedPointer is not null, EventInt(8));
                    try { sub.Callback?.Invoke(snapshot); } catch (Exception ex) { Log(1, $"[{document.OwnerModId}] {type} handler failed: {ex}"); }
                }
            }
        }
        finally { draining = false; }
    }
    private static int EventInt(int field) => int.TryParse(Native.Utf8(Native.vr_event_value(field)), out var value) ? value : 0;
    private static float EventFloat(int field) => float.TryParse(Native.Utf8(Native.vr_event_value(field)), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
    internal int SubscriptionCount => subscriptions.Count;
    internal void Notify(Action? action)
    {
        if (action is null) return;
        foreach (Action callback in action.GetInvocationList()) try { callback(); } catch (Exception ex) { Log(1, $"Window callback failed: {ex}"); }
    }
    private int Read(int kind, string path, out nint data, out int length, out int width, out int height)
    {
        data = 0; length = width = height = 0;
        try
        {
            byte[] bytes;
            switch (kind)
            {
                case 0: bytes = host.ReadAsset(RmlAssetPath.Normalize(path)); break;
                case 1: (bytes, width, height) = host.ReadImage(RmlAssetPath.Normalize(path)); break;
                case 2: bytes = Encoding.UTF8.GetBytes(host.Translate(path)); break;
                case 3: bytes = Encoding.UTF8.GetBytes(host.Clipboard); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            length = bytes.Length; data = Marshal.AllocHGlobal(Math.Max(1, length)); Marshal.Copy(bytes, 0, data, length); return 1;
        }
        catch (Exception ex)
        {
            if (data != 0) Marshal.FreeHGlobal(data);
            data = 0; length = width = height = 0;
            Log(1, $"Resource '{path}' failed: {ex.Message}"); return 0;
        }
    }
    private void Log(int level, string message) { try { host.Log(level, message); } catch { /* Never unwind managed exceptions through C++. */ } }
    private void Write(int kind, string value)
    {
        try { if (kind == 0) host.Clipboard = value; else if (kind == 1) host.Cursor = value; }
        catch (Exception ex) { Log(1, ex.Message); }
    }
    public void Dispose()
    {
        if (!IsAvailable || disposing) return;
        CheckThread(); disposing = true;
        foreach (var document in documents.Values.ToArray()) document.Dispose();
        Native.vr_shutdown(); Native.Check(); IsAvailable = false;
        GC.KeepAlive(callbacks);
    }
    private sealed class Subscription(RmlRuntime runtime, ulong id, RmlDocument document, Action<RmlEvent> callback) : IDisposable
    {
        public ulong Id => id;
        public RmlDocument Document => document;
        public Action<RmlEvent>? Callback { get; private set; } = callback;
        public void Detach() => Callback = null;
        public void Dispose()
        {
            if (Callback is null) return;
            runtime.CheckThread(); Native.vr_unlisten(id); Native.Check(); runtime.subscriptions.Remove(id); Detach();
        }
    }
}
