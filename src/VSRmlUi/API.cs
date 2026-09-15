// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

namespace VSRmlUi;

public sealed class RmlUiException(string message) : Exception(message);

public enum RmlWindowMode { Window, Modal, Hud }

public sealed class RmlInputPolicy
{
    public bool ReceiveMouse { get; set; } = true;
    public bool ReceiveKeyboard { get; set; } = true;
    public bool UnlockMouse { get; set; } = true;
    public bool CapturePointer { get; set; } = true;
    public bool MapToViewport { get; set; } = true;
}

public readonly record struct RmlViewport(int X, int Y, int Width, int Height, float Scale = 1);
public enum RmlDrawTarget { Screen, Offscreen }

/// <summary>Pixels follow the framebuffer. Use dp units in RCSS to follow the game's GUI scale.</summary>
public sealed record RmlDocumentOptions
{
    public RmlWindowMode Mode { get; init; } = RmlWindowMode.Window;
    public bool CloseOnEscape { get; init; } = true;
    public bool UnlockMouse { get; init; } = true;
    public double DrawOrder { get; init; } = 0.2;
    public RmlDrawTarget DrawTarget { get; init; } = RmlDrawTarget.Screen;
    public RmlInputPolicy Input { get; init; } = new();
}

/// <summary>Client-thread API. A service belongs to one world; never retain it after leaving that world.</summary>
public interface IRmlUiService
{
    bool IsAvailable { get; }
    string Version { get; }
    string RmlUiVersion { get; }
    RmlDocument LoadDocument(string ownerModId, string assetPath, RmlDocumentOptions? options = null);
    RmlDocument LoadDocumentFromString(string ownerModId, string markup, string sourcePath, RmlDocumentOptions? options = null);
    void RegisterFont(string assetPath, string family, int weight = 400, bool italic = false, bool fallback = false);
    void CloseAll(string ownerModId);
    void ReleaseAll(string ownerModId);
    bool TryGetDocument(object host, out RmlDocument? document);
    void AttachHost(object host, RmlDocument document);
}

/// <summary>Immutable snapshot delivered after native event dispatch. It cannot cancel RmlUi default actions.</summary>
public sealed record RmlEvent(string Type, string Value, string TargetId, RmlElement Target, RmlDocument Document,
    int MouseX = 0, int MouseY = 0, int Button = -1, float Wheel = 0, int Modifiers = 0, bool Captured = false, int Key = 0);

/// <summary>Normalizes Vintage Story asset addresses, including relative paths supplied by RmlUi.</summary>
public static class RmlAssetPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('\\', '/');
        if (path.Contains("://", StringComparison.Ordinal)) throw new ArgumentException("RmlUi assets must use modid:path, not network URLs.", nameof(path));
        int colon = path.IndexOf(':');
        if (colon <= 0 || path.IndexOf(':', colon + 1) >= 0) throw new ArgumentException("Use modid:path/to/file for RmlUi assets.", nameof(path));
        string domain = path[..colon];
        if (!domain.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) throw new ArgumentException("Invalid asset domain.", nameof(path));
        List<string> parts = [];
        foreach (string segment in path[(colon + 1)..].Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (parts.Count == 0) throw new ArgumentException("Asset path escapes its domain.", nameof(path));
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                if (segment.Any(c => c is '\0' or '?' or '#' || char.IsControl(c))) throw new ArgumentException("Invalid asset path.", nameof(path));
                parts.Add(segment);
            }
        }
        if (parts.Count == 0) throw new ArgumentException("Asset path must include a file.", nameof(path));
        return domain.ToLowerInvariant() + ":" + string.Join('/', parts);
    }
}

/// <summary>Physical context pixels, including scrolling. Framebuffer origin is supplied by the document.</summary>
public readonly record struct RmlElementBounds(float X, float Y, float Width, float Height, float ScrollX, float ScrollY)
{
    public bool Contains(float x, float y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}
public enum RmlInputKind { MouseDown, MouseMove, MouseUp, Wheel, KeyDown, KeyUp }
public readonly record struct RmlInputEvent(RmlInputKind Kind, int X = 0, int Y = 0, int Button = -1, float Wheel = 0, int Key = 0, int Modifiers = 0);
/// <summary>Scopes auxiliary render passes so screen documents never enter offscreen captures.</summary>
public static class RmlRenderScope
{
    [ThreadStatic] private static RmlDrawTarget current;
    public static RmlDrawTarget Current => current;
    public static IDisposable Enter(RmlDrawTarget target) { var previous = current; current = target; return new Scope(previous); }
    private sealed class Scope(RmlDrawTarget previous) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; current = previous; }
    }
}
