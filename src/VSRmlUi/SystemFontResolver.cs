// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Xml.Linq;
using SkiaSharp;
using Vintagestory.API.Client;

namespace VSRmlUi;

/// <summary>Loads the selected game/system typeface into RmlUi without shipping font data.</summary>
internal sealed class SystemFontResolver : IDisposable
{
    private readonly List<Face> faces = [];
    private readonly Action<int, string> log;
    private readonly Func<int, SKTypeface?> matchCharacter;
    private readonly HashSet<string> rejected = new(StringComparer.Ordinal);
    private readonly List<string> systemFontFiles = [];
    private readonly HashSet<string> rejectedSystemFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? preferred;
    private bool warnedMissing;
    private bool disposed;

    internal readonly record struct FontData(byte[] Bytes, int FaceIndex);

    internal SystemFontResolver(string? preferred, string? locale, IEnumerable<byte[]> assets, Action<int, string> log,
        Func<int, SKTypeface?>? matchCharacter = null)
    {
        this.preferred = preferred;
        this.log = log;
        string[] languages = string.IsNullOrEmpty(locale) ? [] : [locale, locale.Replace('-', '_'), locale.Split('-', '_')[0]];
        this.matchCharacter = matchCharacter ?? (character =>
            SKFontManager.Default.MatchCharacter(preferred, languages, character)
            ?? SKFontManager.Default.MatchCharacter(preferred, character)
            ?? SKFontManager.Default.MatchCharacter(character));
        foreach (byte[] bytes in assets)
        {
            using var data = SKData.CreateCopy(bytes);
            var face = SKTypeface.FromData(data);
            if (face is not null) faces.Add(new Face(face, new FontData(bytes, 0)));
        }
        systemFontFiles.AddRange(EnumerateSystemFontFiles());
    }

    internal static void Register(RmlRuntime runtime, ICoreClientAPI api)
    {
        runtime.ConfigureFonts(GuiStyle.StandardFontName, Vintagestory.API.Config.Lang.CurrentLocale,
            api.Assets.GetMany("fonts").Select(asset => asset.Data));
    }

    internal string RegisterDefaults(RmlRuntime runtime)
    {
        Face? primary = FindFamily(preferred, 400, false) ?? FindFamily("Montserrat", 400, false) ?? AddSystem(SKTypeface.CreateDefault());
        if (primary is null) throw new RmlUiException("No readable system or game font was found.");
        var font = primary.Data!.Value;
        runtime.RegisterFontData(font.Bytes, "vsrmlui-default", fallback: true, faceIndex: font.FaceIndex);
        string family = primary.Typeface.FamilyName;
        foreach (var (weight, italic) in new[] { (700, false), (400, true), (700, true) })
        {
            Face? style = FindFamily(family, weight, italic);
            if (style is null) continue;
            var data = style.Data!.Value;
            runtime.RegisterFontData(data.Bytes, "vsrmlui-default", weight, italic, faceIndex: data.FaceIndex);
        }
        log(4, $"Using local font '{family}' for RmlUi; no fonts are bundled.");
        return family;
    }

    internal FontData? Resolve(int character)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var face in faces)
            if (face.Typeface.ContainsGlyph(character) && face.Data is not null) return face.Data;
        var candidate = matchCharacter(character);
        if (candidate is not null)
        {
            if (candidate.ContainsGlyph(character))
            {
                var face = AddSystem(candidate);
                if (face is not null) return face.Data;
            }
            else candidate.Dispose();
        }
        var fileFace = FindSystemFile(character);
        if (fileFace is not null) return fileFace.Data;
        if (!warnedMissing)
        {
            warnedMissing = true;
            log(3, $"No readable local font covers U+{character:X4}. Install a system font for the required language (for example Noto CJK on Linux), then restart the client. Further missing-glyph warnings are suppressed for this world.");
        }
        return null;
    }

    private Face? FindSystemFile(int character)
    {
        foreach (string path in systemFontFiles)
        {
            if (rejectedSystemFiles.Contains(path)) continue;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var data = SKData.CreateCopy(bytes);
                bool matched = false;
                for (int index = 0; index < 64; index++)
                {
                    var typeface = SKTypeface.FromData(data, index);
                    if (typeface is null) break;
                    if (typeface.ContainsGlyph(character))
                    {
                        var face = AddSystem(typeface);
                        if (face is not null) return face;
                        matched = true;
                    }
                    else typeface.Dispose();
                }
                if (!matched) rejectedSystemFiles.Add(path);
            }
            catch { rejectedSystemFiles.Add(path); }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateSystemFontFiles()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDirectories(Environment.GetEnvironmentVariable("VSRMLUI_FONT_DIRS"), directories);
        if (OperatingSystem.IsWindows())
            AddDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"), directories);
        else if (OperatingSystem.IsMacOS())
        {
            AddDirectories("/System/Library/Fonts", directories);
            AddDirectories("/Library/Fonts", directories);
            AddDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts"), directories);
        }
        else
        {
            AddDirectories("/usr/share/fonts", directories);
            AddDirectories("/usr/local/share/fonts", directories);
            AddDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/fonts"), directories);
            AddDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"), directories);
            string? config = Environment.GetEnvironmentVariable("FONTCONFIG_FILE");
            if (!string.IsNullOrWhiteSpace(config) && File.Exists(config))
            {
                try
                {
                    var document = XDocument.Load(config);
                    foreach (var dir in document.Descendants("dir"))
                    {
                        string value = Environment.ExpandEnvironmentVariables(dir.Value.Trim());
                        if (value.StartsWith("~", StringComparison.Ordinal))
                            value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[1..].TrimStart('/', '\\'));
                        AddDirectories(value, directories);
                    }
                }
                catch { }
            }
        }

        foreach (string directory in directories)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (string file in files)
                if (FontExtensions.Contains(Path.GetExtension(file))) yield return file;
        }
    }

    private static void AddDirectories(string? value, ISet<string> directories)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (string item in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string path = Environment.ExpandEnvironmentVariables(item);
            if (Directory.Exists(path)) directories.Add(Path.GetFullPath(path));
        }
    }

    private static class FontExtensions
    {
        internal static bool Contains(string extension) => extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otc", StringComparison.OrdinalIgnoreCase);
    }

    private Face? FindFamily(string? family, int weight, bool italic)
    {
        if (string.IsNullOrWhiteSpace(family)) return null;
        var asset = faces.Where(face => face.Typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
            .OrderBy(face => Math.Abs(face.Typeface.FontWeight - weight) + (face.Typeface.IsItalic == italic ? 0 : 1000)).FirstOrDefault();
        if (asset is not null && asset.Typeface.FontWeight == weight && asset.Typeface.IsItalic == italic) return asset;
        using var style = new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var system = SKFontManager.Default.MatchFamily(family, style);
        if (system is not null && !system.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase)) { system.Dispose(); system = null; }
        return AddSystem(system) ?? FindSystemFile(family, weight, italic) ?? asset;
    }

    private Face? FindSystemFile(string family, int weight, bool italic)
    {
        SKTypeface? best = null;
        int bestScore = int.MaxValue;
        foreach (string path in systemFontFiles)
        {
            if (rejectedSystemFiles.Contains(path)) continue;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var data = SKData.CreateCopy(bytes);
                for (int index = 0; index < 64; index++)
                {
                    var typeface = SKTypeface.FromData(data, index);
                    if (typeface is null) break;
                    if (!MatchesFamily(typeface, family)) { typeface.Dispose(); continue; }
                    int score = Math.Abs(typeface.FontWeight - weight) + (typeface.IsItalic == italic ? 0 : 1000);
                    if (score < bestScore)
                    {
                        best?.Dispose();
                        best = typeface;
                        bestScore = score;
                    }
                    else typeface.Dispose();
                }
            }
            catch { rejectedSystemFiles.Add(path); }
        }
        return AddSystem(best);
    }

    private static bool MatchesFamily(SKTypeface typeface, string family)
    {
        string wanted = NormalizeFamily(family);
        return NormalizeFamily(typeface.FamilyName) == wanted
            || NormalizeFamily(typeface.PostScriptName) == wanted;
    }

    private static string NormalizeFamily(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private Face? AddSystem(SKTypeface? typeface)
    {
        if (typeface is null) return null;
        string name = $"{typeface.FamilyName}|{typeface.PostScriptName}|{typeface.FontWeight}|{typeface.FontSlant}";
        if (rejected.Contains(name)) { typeface.Dispose(); return null; }
        try
        {
            var data = Read(typeface);
            // A TTC may contain several different faces with identical backing bytes.
            string identity = Convert.ToHexString(SHA256.HashData(data.Bytes)) + "/" + data.FaceIndex;
            var existing = faces.FirstOrDefault(face => face.Identity == identity);
            if (existing is not null) { typeface.Dispose(); return existing; }
            var result = new Face(typeface, data) { Identity = identity };
            faces.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            rejected.Add(name);
            log(3, $"Cannot read local font '{typeface.FamilyName}': {ex.Message}");
            typeface.Dispose();
            return null;
        }
    }

    internal static unsafe FontData Read(SKTypeface typeface)
    {
        using SKStreamAsset? stream = typeface.OpenStream(out int faceIndex);
        if (stream is null || !stream.HasLength || stream.Length <= 0 || !stream.Rewind())
            throw new IOException("The font provider did not expose readable font data.");
        byte[] bytes = new byte[stream.Length];
        fixed (byte* start = bytes)
        {
            for (int offset = 0; offset < bytes.Length;)
            {
                int read = stream.Read((nint)(start + offset), bytes.Length - offset);
                if (read <= 0) throw new EndOfStreamException("Incomplete font stream.");
                offset += read;
            }
        }
        return new FontData(bytes, faceIndex);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var face in faces) face.Typeface.Dispose();
        faces.Clear();
    }

    private sealed class Face(SKTypeface typeface, FontData data)
    {
        public SKTypeface Typeface { get; } = typeface;
        public FontData? Data { get; } = data;
        public string? Identity { get; init; }
    }
}
