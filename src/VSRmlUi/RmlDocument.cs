// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

namespace VSRmlUi;

/// <summary>A retained RML document. Hide/Close keeps its DOM; Dispose destroys it and all subscriptions.</summary>
public sealed class RmlDocument : IDisposable
{
    internal RmlDocument(RmlRuntime runtime, ulong handle, string owner, string source, RmlDocumentOptions options)
    { Runtime = runtime; Handle = handle; OwnerModId = owner; SourcePath = source; Options = options; Root = new(this, 0); }
    internal RmlRuntime Runtime { get; }
    internal ulong Handle { get; }
    internal IDocumentView? View { get; set; }
    public string OwnerModId { get; }
    public string SourcePath { get; }
    public RmlDocumentOptions Options { get; }
    public RmlViewport Viewport { get; private set; }
    public RmlDrawTarget DrawTarget => Options.DrawTarget;
    public RmlElement Root { get; }
    public bool IsVisible { get; private set; }
    public bool IsDisposed { get; private set; }
    public event Action? Closed;
    public object? Host => View;
    public event Action? InputCancelled;
    /// <summary>Runs on the client thread before native dispatch. True consumes this input in both RmlUi and the game. No callback crosses a native stack.</summary>
    public Func<RmlInputEvent, bool>? InputFilter { get; set; }
    public RmlElement? CapturedPointer { get; private set; }
    public void CapturePointer(RmlElement element) { EnsureAlive(); if (element.Document != this) throw new ArgumentException("Element belongs to another document."); _ = element.Id; CapturedPointer = element; }
    public void ReleasePointer() { EnsureAlive(); CapturedPointer = null; }
    internal void CancelInput() { CapturedPointer = null; Runtime.Notify(InputCancelled); }
    internal bool Filter(RmlInputEvent input) { try { return InputFilter?.Invoke(input) == true; } catch { CancelInput(); throw; } }
    public (float X, float Y) ToFramebuffer(float x, float y) => (Viewport.X + x * Viewport.Scale, Viewport.Y + y * Viewport.Scale);
    public (float X, float Y) FromFramebuffer(float x, float y) => ((x - Viewport.X) / Viewport.Scale, (y - Viewport.Y) / Viewport.Scale);

    public bool UsesWindowViewport { get; private set; } = true;
    public void SetViewport(RmlViewport viewport)
    {
        EnsureAlive();
        if (viewport.Width <= 0 || viewport.Height <= 0 || !float.IsFinite(viewport.Scale) || viewport.Scale <= 0) throw new ArgumentOutOfRangeException(nameof(viewport));
        UsesWindowViewport = false; ApplyViewport(viewport);
    }
    public void UseWindowViewport() { EnsureAlive(); UsesWindowViewport = true; UpdateViewport(); }
    internal void UpdateViewport()
    {
        var size = Runtime.Dimensions();
        ApplyViewport(UsesWindowViewport ? new RmlViewport(0, 0, size.Width, size.Height, size.Scale) : Viewport);
    }
    private void ApplyViewport(RmlViewport viewport)
    {
        Viewport = viewport;
        Call(16, viewport.X, Runtime.Dimensions().Height - viewport.Y - viewport.Height);
        Call(3, viewport.Width, viewport.Height, viewport.Scale);
    }


    public RmlElement? GetElementById(string id) => Root.GetElementById(id);
    public RmlElement? QuerySelector(string selector) => Root.QuerySelector(selector);
    public void Show()
    {
        EnsureAlive();
        Call(1, Options.Mode == RmlWindowMode.Modal ? 1 : 0, Options.Mode == RmlWindowMode.Hud ? 1 : 0);
        IsVisible = true;
        if (View is not null && !View.Open()) { CancelInput(); Call(2); IsVisible = false; throw new RmlUiException("The game declined to open the dialog."); }
        Runtime.DrainEvents();
    }
    public void Hide() => Close();
    public void Close()
    {
        EnsureAlive();
        if (!IsVisible) return;
        CancelInput(); Call(2); IsVisible = false; View?.Close();
        Runtime.Notify(Closed);
        Runtime.DrainEvents();
    }
    internal void EnsureAlive()
    {
        Runtime.CheckThread();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
    internal long Call(int op, int a = 0, int b = 0, float value = 0, string? text = null)
    {
        EnsureAlive();
        long result = Native.vr_doc(Handle, op, a, b, value, text); Native.Check(); return result;
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        EnsureAlive();
        CancelInput(); InputFilter = null; InputCancelled = null; bool wasVisible = IsVisible;
        IsVisible = false; IsDisposed = true;
        View?.Dispose(); View = null;
        Runtime.Remove(this);
        Native.vr_doc(Handle, 0, 0, 0, 0, null); Native.Check();
        if (wasVisible) Runtime.Notify(Closed);
        Closed = null;
    }
}

internal interface IDocumentView : IDisposable { bool Open(); void Close(); }
