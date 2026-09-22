// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using SkiaSharp;
using VSRmlUi;

internal static class SystemFontChecks
{
    internal static void Run(string root, string gameRoot, Action<bool, string> check)
    {
        byte[] montserrat = File.ReadAllBytes(Path.Combine(gameRoot, "assets/game/fonts/Montserrat-Regular.ttf"));
        byte[] lora = File.ReadAllBytes(Path.Combine(gameRoot, "assets/game/fonts/Lora-Regular.ttf"));
        check(!Directory.EnumerateFiles(Path.Combine(root, "src/VSRmlUi/assets"), "*", SearchOption.AllDirectories)
            .Any(path => new[] { ".ttf", ".otf", ".ttc", ".otc", ".woff", ".woff2" }.Contains(Path.GetExtension(path).ToLowerInvariant())),
            "RmlUi assets contain no bundled font files");

        var host = new FontHost();
        using (var runtime = new RmlRuntime(host, headless: true))
        {
            byte[] collection = Collection(montserrat, lora);
            using var data = SKData.CreateCopy(collection);
            using var face = SKTypeface.FromData(data, 1);
            check(face is not null && face.FamilyName == "Lora", "TTC fixture exposes a distinct second face");
            var exported = SystemFontResolver.Read(face!);
            check(exported.FaceIndex == 1, "Skia font stream retains the nonzero TTC face index");
            runtime.RegisterFontData(exported.Bytes, "collection", faceIndex: exported.FaceIndex);
            runtime.RegisterFontData(lora, "standalone");
            runtime.RegisterFontData(collection, "first-face");
            using var document = runtime.LoadDocumentFromString("fontchecks", """
                <rml><head><style>span { display: inline-block; font-size: 32px; }</style></head><body>
                <span id="a" style="font-family: collection;">Wide font metrics 123</span>
                <span id="b" style="font-family: standalone;">Wide font metrics 123</span>
                <span id="c" style="font-family: first-face;">Wide font metrics 123</span>
                </body></rml>
                """, "fontchecks:collection.rml");
            document.Show(); document.Call(3, 1000, 800, 1);
            float a = document.GetElementById("a")!.Bounds.Width, b = document.GetElementById("b")!.Bounds.Width;
            float c = document.GetElementById("c")!.Bounds.Width;
            check(a > 0 && Math.Abs(a - b) < 0.01f && Math.Abs(a - c) > 1,
                "native font registration uses TTC face 1 instead of face 0");
        }

        using (var runtime = new RmlRuntime(host, headless: true))
        {
            runtime.ConfigureFonts("Lora", "en", [montserrat, lora]);
            check(host.Messages.Any(m => m.Text.Contains("Using local font 'Lora'")), "selected game font takes precedence over Montserrat");
        }
        using (var runtime = new RmlRuntime(host, headless: true))
        {
            runtime.ConfigureFonts("font-family-that-does-not-exist", "en", []);
            using var document = runtime.LoadDocumentFromString("fontchecks", "<rml><body>System default</body></rml>", "fontchecks:default.rml");
            document.Show(); document.Call(3, 800, 600, 1);
            check(runtime.IsAvailable, "missing game fonts fall back to a readable system default");
        }

        var missingHost = new FontHost();
        int lookups = 0;
        using (var runtime = new RmlRuntime(missingHost, headless: true))
        {
            runtime.ConfigureFonts("Montserrat", "en", [montserrat], _ => { lookups++; return null; });
            using var document = runtime.LoadDocumentFromString("fontchecks", "<rml><body><span id='text'>Latin text</span></body></rml>", "fontchecks:missing.rml");
            document.Show();
            document.GetElementById("text")!.Text = "Missing " + char.ConvertFromUtf32(0x10ffff);
            for (int i = 0; i < 4; i++) document.Call(3, 800, 600, 1);
            check(lookups == 1, "missing supplementary glyph is resolved only once across layout passes");
            document.GetElementById("text")!.Text = "Still running " + char.ConvertFromUtf32(0x10fffe);
            document.Call(3, 800, 600, 1);
            check(lookups == 2 && runtime.IsAvailable, "unavailable glyphs do not disable the UI");
            check(missingHost.Messages.Count(m => m.Level == 3 && m.Text.Contains("No readable local font")) == 1,
                "missing local fonts emit one actionable warning per world");
        }

        using var cjk = SKFontManager.Default.MatchCharacter(0x4e2d);
        if (cjk is null || !cjk.ContainsGlyph(0x4e2d))
        {
            string? fontDirs = Environment.GetEnvironmentVariable("VSRMLUI_FONT_DIRS");
            if (string.IsNullOrWhiteSpace(fontDirs))
                throw new Exception("System font checks require an installed Chinese font (for example fonts-noto-cjk on Linux).");
        }
        var dynamicHost = new FontHost();
        var requests = new HashSet<int>();
        using (var runtime = new RmlRuntime(dynamicHost, headless: true))
        {
            runtime.ConfigureFonts("Montserrat", "zh-cn", [montserrat], character =>
            {
                requests.Add(character);
                return SKFontManager.Default.MatchCharacter(character);
            });
            using var document = runtime.LoadDocument("fontchecks", "fontchecks:dynamic.rml");
            document.Show(); document.Call(3, 800, 600, 1);
            check(requests.Contains(0x4e2d), "translated RML resolves a local fallback font at layout");
            document.GetElementById("text")!.Text = "\u6c49";
            document.GetElementById("entry")!.Value = "\u5b57";
            document.GetElementById("entry")!.Focus();
            document.Call(11, text: "\u8f93");
            dynamicHost.Clipboard = "\u7c98";
            document.Call(9, 33, 1); // RmlUi KI_V with Control.
            document.Call(3, 800, 600, 1);
            check(requests.Count == 1, "resolved CJK font is reused across dynamic text, control values and typing");
            check(document.GetElementById("entry")!.Value.Contains("\u7c98"), "native clipboard paste reaches the text widget");
            check(!dynamicHost.Messages.Any(m => m.Level <= 3), "dynamic system font loading produces no warnings");
        }
        foreach (string operation in new[] { "Text", "InnerRml", "Value", "Typing", "Paste" })
        {
            var operationHost = new FontHost();
            int queried = 0;
            using var runtime = new RmlRuntime(operationHost, headless: true);
            runtime.ConfigureFonts("Montserrat", "zh-cn", [montserrat], character =>
            {
                if (character == 0x4e2d) queried++;
                return SKFontManager.Default.MatchCharacter(character);
            });
            using var document = runtime.LoadDocumentFromString("fontchecks",
                "<rml><head><style>input { width: 300px; height: 32px; font-family: vsrmlui-default; font-size: 18px; }</style></head>"
                + "<body><span id='text'>Latin</span><input id='entry' type='text'/></body></rml>", "fontchecks:mutation.rml");
            document.Show(); document.Call(3, 800, 600, 1);
            var entry = document.GetElementById("entry")!;
            switch (operation)
            {
                case "Text": document.GetElementById("text")!.Text = "\u4e2d"; break;
                case "InnerRml": document.GetElementById("text")!.InnerRml = "&#x4e2d;"; break;
                case "Value": entry.Value = "\u4e2d"; break;
                case "Typing": entry.Focus(); document.Call(11, text: "\u4e2d"); break;
                case "Paste": entry.Focus(); operationHost.Clipboard = "\u4e2d"; document.Call(9, 33, 1); break;
            }
            document.Call(3, 800, 600, 1); document.Call(4);
            check(queried == 1 && !operationHost.Messages.Any(m => m.Level <= 3),
                operation + " discovers a new local font after the document is open (lookups=" + queried + ")");
        }
        check(!host.Messages.Any(m => m.Level <= 3), "collection and default font checks produce no warnings");
    }

    internal static void Render(RmlRuntime runtime, Func<byte[]> pixels, Action clear, Action<bool, string> check)
    {
        using var document = runtime.LoadDocumentFromString("fontchecks", """
            <rml><head><style>body { font-family: vsrmlui-default; font-size: 44px; color: #ffffff; }
            #glyph { position: absolute; left: 10px; top: 10px; }</style></head><body><span id="glyph"></span></body></rml>
            """, "fontchecks:render.rml");
        document.Show();
        byte[] Draw(string text)
        {
            document.GetElementById("glyph")!.Text = text;
            clear(); document.Call(3, 1000, 800, 1); document.Call(4);
            return pixels();
        }
        byte[] empty = Draw("");
        byte[] replacement = Draw("\ufffd");
        byte[] chinese = Draw("\u4e2d");
        check(!chinese.SequenceEqual(empty) && !chinese.SequenceEqual(replacement), "system Chinese glyph renders pixels distinct from the missing-glyph replacement");
    }

    // Build a TTC from installed test inputs so no font binaries enter the repository.
    private static byte[] Collection(params byte[][] fonts)
    {
        int offset = 12 + 4 * fonts.Length;
        byte[] data = new byte[offset + fonts.Sum(font => (font.Length + 3) & ~3)];
        "ttcf"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)fonts.Length);
        for (int i = 0; i < fonts.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12 + 4 * i), (uint)offset);
            fonts[i].CopyTo(data, offset);
            int tables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 4));
            for (int table = 0; table < tables; table++)
            {
                var tableOffset = data.AsSpan(offset + 12 + table * 16 + 8, 4);
                BinaryPrimitives.WriteUInt32BigEndian(tableOffset, BinaryPrimitives.ReadUInt32BigEndian(tableOffset) + (uint)offset);
            }
            offset += (fonts[i].Length + 3) & ~3;
        }
        return data;
    }

    private sealed class FontHost : IRmlHost
    {
        public List<(int Level, string Text)> Messages { get; } = [];
        public byte[] ReadAsset(string path) => path == "fontchecks:dynamic.rml" ? System.Text.Encoding.UTF8.GetBytes(
            "<rml><body><span id='text'>[[fontchecks:translation]] &#x6587;</span><input id='entry' type='text'/></body></rml>") : throw new FileNotFoundException(path);
        public (byte[] Pixels, int Width, int Height) ReadImage(string path) => throw new FileNotFoundException(path);
        public string Translate(string input) => input.Replace("[[fontchecks:translation]]", "\u4e2d");
        public string Clipboard { get; set; } = "";
        public string Cursor { set { } }
        public void Log(int level, string message) => Messages.Add((level, message));
    }
}
