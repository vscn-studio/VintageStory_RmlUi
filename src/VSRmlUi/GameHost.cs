// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VSRmlUi;

internal sealed partial class GameHost(ICoreClientAPI api) : IRmlHost
{
    internal string CurrentCursor { get; private set; } = "normal";
    public byte[] ReadAsset(string path)
    {
        var asset = api.Assets.TryGet(new AssetLocation(path));
        if (asset is not null) return asset.Data;
        throw new FileNotFoundException($"Asset not found: {path}");
    }
    public (byte[] Pixels, int Width, int Height) ReadImage(string path)
    {
        using SKData data = SKData.CreateCopy(ReadAsset(path));
        using SKCodec codec = SKCodec.Create(data) ?? throw new RmlUiException($"Unsupported image: {path}");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 64 * 1024 * 1024) throw new RmlUiException("Invalid or oversized image.");
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) throw new RmlUiException($"Unable to decode image: {path}");
        byte[] pixels = new byte[checked(info.Width * info.Height * 4)];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return (pixels, info.Width, info.Height);
    }
    // Explicit tokens leave normal RML text and data-binding expressions untouched.
    public string Translate(string input) => TranslationToken().Replace(input, match => Lang.Get(match.Groups[1].Value));
    [GeneratedRegex(@"\[\[([a-zA-Z0-9_-]+:[^\[\]\r\n]+)\]\]")]
    private static partial Regex TranslationToken();
    public string Clipboard { get => api.Input.ClipboardText ?? ""; set => api.Input.ClipboardText = value; }
    public string Cursor { set => CurrentCursor = value switch { "pointer" => "linkselect", "text" => "textselect", _ => "normal" }; }
    public void Log(int level, string message)
    {
        if (level <= 2) api.Logger.Error("[vsrmlui] {0}", message);
        else if (level == 3) api.Logger.Warning("[vsrmlui] {0}", message);
        else api.Logger.Debug("[vsrmlui] {0}", message);
    }
}
