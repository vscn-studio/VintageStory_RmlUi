// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

namespace VSRmlUi;

/// <summary>A weak native element handle. Operations throw after document disposal or DOM removal.</summary>
public sealed class RmlElement
{
    internal RmlElement(RmlDocument document, ulong handle) { Document = document; Handle = handle; }
    public RmlDocument Document { get; }
    internal ulong Handle { get; }
    public RmlElementBounds Bounds
    {
        get { var v = Get(6).Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); return new(v[0], v[1], v[2], v[3], v[4], v[5]); }
    }
    public (float X, float Y) ToLocal(float framebufferX, float framebufferY)
    { var b = Bounds; return (framebufferX - Document.Viewport.X - b.X + b.ScrollX, framebufferY - Document.Viewport.Y - b.Y + b.ScrollY); }
    public void CapturePointer() => Document.CapturePointer(this);
    public void ReleasePointer() => Document.ReleasePointer();
    public string Id => Get(4);
    public string TagName => Get(5);
    public string InnerRml { get => Get(0); set => Apply(2, value: value); }
    /// <summary>Sets literal text, escaping markup through a native text node.</summary>
    public string Text { set => Apply(15, value: value); }
    public string ClassNames { get => Get(2); set => Apply(7, value: value); }
    public string Value { get => Get(3); set => Apply(9, value: value); }
    public string GetAttribute(string name) => Get(1, name);
    public void SetAttribute(string name, string value) => Apply(3, name, value);
    public void RemoveAttribute(string name) => Apply(4, name);
    public void SetProperty(string name, string value) => Apply(5, name, value);
    public void RemoveProperty(string name) => Apply(6, name);
    public void SetClass(string name, bool enabled) => Apply(8, name, enabled ? "1" : "0");
    public void Focus() => Apply(10);
    public void Blur() => Apply(11);
    public RmlElement AppendChild(string tag) => new(Document, Apply(12, tag));
    public void Remove() => Apply(13);
    public RmlElement? GetElementById(string id) => Find(0, id);
    public RmlElement? QuerySelector(string selector) => Find(1, selector);
    public unsafe IReadOnlyList<RmlElement> QuerySelectorAll(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector); Document.EnsureAlive(); const int capacity = 4096;
        ulong* handles = stackalloc ulong[capacity]; int count = Native.vr_query_all(Document.Handle, Handle, selector, handles, capacity); Native.Check();
        var result = new List<RmlElement>(Math.Min(count, capacity)); for (int i = 0; i < Math.Min(count, capacity); i++) if (handles[i] != 0) result.Add(new(Document, handles[i])); return result;
    }
    public IDisposable On(string eventType, Action<RmlEvent> callback, bool capture = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType); ArgumentNullException.ThrowIfNull(callback);
        Document.EnsureAlive();
        return Document.Runtime.Listen(this, eventType, callback, capture);
    }
    public void DispatchEvent(string eventType, string value = "") { Apply(14, eventType, value); Document.Runtime.DrainEvents(); }
    private RmlElement? Find(int op, string name) { ulong id = Apply(op, name); return id == 0 ? null : new(Document, id); }
    private ulong Apply(int op, string? name = null, string? value = null)
    {
        Document.EnsureAlive();
        ulong result = Native.vr_element(Document.Handle, Handle, op, name, value); Native.Check(); return result;
    }
    private string Get(int op, string? name = null)
    {
        Document.EnsureAlive();
        nint result = Native.vr_get(Document.Handle, Handle, op, name); Native.Check(); return Native.Utf8(result);
    }
}
