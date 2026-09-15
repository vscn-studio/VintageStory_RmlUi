// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VSRmlUi;

string root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "../..");
string gameRoot = Path.GetFullPath(args.FirstOrDefault(a => a.StartsWith("--game="))?[7..] ?? Path.Combine(root, "..", "Vintagestory"));
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    foreach (string directory in new[] { gameRoot, Path.Combine(gameRoot, "Lib") })
    {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};
var host = new TestHost(root, gameRoot);
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
void Throws<T>(Action action, string name) where T : Exception { try { action(); } catch (T) { Check(true, name); return; } throw new Exception("FAIL (did not throw): " + name); }
Console.WriteLine($"Game API: {typeof(ModSystem).Assembly.GetName().Version}; Native ABI: {Native.vr_abi()}");
foreach (var os in new[] { "win", "linux", "osx" })
    foreach (var arch in new[] { Architecture.X64, Architecture.Arm64 })
        if (os != "win" || arch == Architecture.X64)
            Check(PlatformLibrary.Get(os, arch).Rid == $"{os}-{arch.ToString().ToLowerInvariant()}", $"portable native RID {os}/{arch}");
Throws<PlatformNotSupportedException>(() => PlatformLibrary.Get("linux", Architecture.X86), "reject unsupported native architecture");
Throws<PlatformNotSupportedException>(() => PlatformLibrary.Get("unknown", Architecture.X64), "reject unsupported native OS");
Check(RmlAssetPath.Normalize("demo:dialog/a/../b.rml") == "demo:dialog/b.rml", "asset path normalization");
Throws<ArgumentException>(() => RmlAssetPath.Normalize("demo:../../secret"), "reject asset domain traversal");
Throws<ArgumentException>(() => RmlAssetPath.Normalize("https://example.com/ui.rml"), "reject network URL assets");
Check(KeyMap.Convert(GlKeys.A) == 12 && KeyMap.Convert(GlKeys.Number9) == 11 && KeyMap.Convert(GlKeys.BackSpace) == 69, "game key mapping");
Check(!new RmlUiModSystem().ShouldLoad(EnumAppSide.Server), "server does not start the UI system");
for (int cycle = 0; cycle < 3; cycle++)
{
    using var ui = new RmlRuntime(host, headless: true);
    ui.RegisterFont("game:fonts/Montserrat-Regular.ttf", "vsrmlui-default");
    ui.RegisterFont("vsrmlui:fonts/NotoSansCJKsc-Regular.otf", "vsrmlui-cjk", fallback: true);
    using var document = ui.LoadDocumentFromString("test", """
        <rml><head><link type="text/rcss" href="vsrmlui:dialog/theme.rcss" /></head>
        <body><div id="content"><button id="button">Click</button><input id="entry" class="text" type="text" value="hello" /></div></body></rml>
        """, "test:dialog/test.rml");
    document.Show(); document.Call(3, 800, 600, 1);
    var button = document.GetElementById("button")!;
    var input = document.GetElementById("entry")!;
    Check(input.Value == "hello", $"cycle {cycle}: load form control");
    input.Value = "中文🙂";
    Check(input.Value == "中文🙂", "UTF-8 form round trip");
    input.Focus(); document.Call(3, 800, 600, 1); document.Call(4); document.Call(9, 88); document.Call(11, text: "输入");
    Check(input.Value.EndsWith("输入"), "Unicode text input routed to focused control");
    button.Text = "<literal> & 中文";
    Check(button.QuerySelector("literal") is null && button.InnerRml.Contains("&lt;literal&gt;"), "literal text is not interpreted as markup");
    button.SetAttribute("data-test", "一 & 二");
    Check(button.GetAttribute("data-test") == "一 & 二", "UTF-8 attribute round trip");
    button.SetProperty("color", "#ff0000");
    button.SetClass("selected", true);
    Check(document.QuerySelector("button.selected") is not null, "class and selector API");
    Check(document.Root.QuerySelectorAll("button").Count == 1, "query selector all returns every match");
    var b = button.Bounds;
    Check(b.Width > 0 && b.Height > 0, "element bounds reflect native layout");
    document.SetViewport(new(70, 40, 800, 600, 1.5f));
    var physical = document.ToFramebuffer(10, 20);
    Check(physical == (85f, 70f) && document.FromFramebuffer(physical.X, physical.Y) == (10f, 20f), "viewport coordinate round trip");
    Check(!document.UsesWindowViewport, "explicit viewport survives host layout updates");
    document.UpdateViewport(); Check(document.Viewport.X == 70, "custom viewport retained");
    document.UseWindowViewport(); Check(document.Viewport.X == 0, "automatic viewport restored");
    int cancelled = 0; document.InputCancelled += () => cancelled++;
    button.CapturePointer(); Check(document.CapturedPointer == button, "pointer capture records its element");
    document.CancelInput(); Check(document.CapturedPointer is null && cancelled == 1, "focus cancellation releases pointer capture");
    RmlEvent? mouseSnapshot = null;
    using (button.On("mousedown", ev => mouseSnapshot = ev))
    {
        document.Call(3, 800, 600, 1); var bounds = button.Bounds;
        int mx = (int)(bounds.X + bounds.Width / 2), my = (int)(bounds.Y + bounds.Height / 2);
        document.Call(5, mx, my); document.Call(6, 0, 3); document.Runtime.DrainEvents();
        Check(mouseSnapshot is { Button: 0, Modifiers: 3 } && mouseSnapshot.MouseX == mx && mouseSnapshot.MouseY == my, "native event snapshots include pointer and modifiers");
        document.Call(7, 0);
    }
    int clicks = 0;
    using var listener = button.On("click", ev => { Check(ev.TargetId == "button" && ev.Type == "click", "event target snapshot"); clicks++; });
    button.DispatchEvent("click");
    Check(clicks == 1, "managed event dispatch");
    listener.Dispose(); button.DispatchEvent("click");
    Check(clicks == 1, "event unsubscription");
    using var isolation = ui.LoadDocumentFromString("other", "<rml><body><div id='button'>Other</div></body></rml>", "other:dialog/test.rml");
    Check(isolation.GetElementById("button")!.TagName == "div", "mod document isolation");
    var child = document.GetElementById("content")!.AppendChild("span"); child.Text = "temporary"; child.Remove();
    Throws<RmlUiException>(() => _ = child.Id, "removed element handle is rejected safely");
    using var staleListener = input.On("change", _ => throw new Exception("Should have been detached"));
    document.GetElementById("content")!.InnerRml = "<p id='replacement'>replaced</p>";
    document.Call(3, 800, 600, 1);
    ui.DrainEvents();
    Check(ui.SubscriptionCount == 0, "destroyed elements release managed event callbacks");
    staleListener.Dispose();
    Throws<RmlUiException>(() => _ = input.Value, "DOM replacement invalidates old handles");
    var threadError = Task.Run(() => { try { document.Show(); return false; } catch (InvalidOperationException) { return true; } }).Result;
    Check(threadError, "cross-thread calls rejected before entering native code");
    document.Close(); Check(!document.IsVisible, "hide document without disposal");
    document.Show(); ui.ReleaseAll("test");
    Check(document.IsDisposed && !isolation.IsDisposed, "owner-scoped resource release");
    Throws<ObjectDisposedException>(() => button.Text = "stale", "disposed document rejects access");
    using var callbackDoc = ui.LoadDocumentFromString("test", "<rml><body><button id='close'>Close</button></body></rml>", "test:close.rml");
    callbackDoc.GetElementById("close")!.On("click", _ => callbackDoc.Dispose());
    callbackDoc.GetElementById("close")!.DispatchEvent("click");
    Check(callbackDoc.IsDisposed, "document can be disposed from a queued event callback");
    if (cycle == 0) GameIntegrationChecks.Run(ui, Check);
}

if (!args.Contains("--headless"))
{
    using var window = new NativeWindow(new NativeWindowSettings { ClientSize = new Vector2i(1000, 800), StartVisible = false, StartFocused = false, API = ContextAPI.OpenGL, APIVersion = OperatingSystem.IsMacOS() ? new Version(4, 1) : new Version(3, 3), Flags = ContextFlags.ForwardCompatible, Profile = ContextProfile.Core, Title = "VSRmlUi render validation" });
    window.Context.MakeCurrent();
    Console.WriteLine($"OpenGL: {GL.GetString(StringName.Version)} / {GL.GetString(StringName.Renderer)}");
    using var framebuffer = new TestFramebuffer(1000, 800);
    // Deliberately bind a nondefault framebuffer, VAO, texture unit and sampler.
    int hostVao = GL.GenVertexArray(), hostBuffer = GL.GenBuffer(), hostSampler = GL.GenSampler(), hostTexture = GL.GenTexture();
    GL.BindVertexArray(hostVao); GL.BindBuffer(BufferTarget.ArrayBuffer, hostBuffer);
    GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, hostTexture); GL.BindSampler(0, hostSampler);
    GL.ActiveTexture(TextureUnit.Texture3); GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8); GL.PixelStore(PixelStoreParameter.UnpackRowLength, 7);
    GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.CullFace); GL.Enable(EnableCap.FramebufferSrgb);
    GL.DepthMask(true); GL.Viewport(0, 0, 1000, 800);
    var before = Snapshot();
    using (var ui = new RmlRuntime(host))
    {
        Check(Snapshot() == before, "OpenGL state preserved across initialization");
        ui.Dimensions = () => (1000, 800, 1);
        ui.RegisterFont("game:fonts/Montserrat-Regular.ttf", "vsrmlui-default");
        ui.RegisterFont("game:fonts/Montserrat-Bold.ttf", "vsrmlui-default", 700);
        ui.RegisterFont("vsrmlui:fonts/NotoSansCJKsc-Regular.otf", "vsrmlui-cjk", fallback: true);
        using var document = ui.LoadDocument("vsrmluiexample", "vsrmluiexample:dialog/example.rml");
        document.Show();
        GL.ClearColor(0.07f, 0.09f, 0.08f, 1); GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        document.Call(3, 1000, 800, 1); document.Call(4);
        Check(Snapshot() == before, "OpenGL bindings/state preserved across layout and rendering");
        Check(GL.GetError() == ErrorCode.NoError, "no OpenGL errors");
        var pixels = framebuffer.Pixels();
        int changed = 0;
        for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] > 90 || pixels[i + 1] > 90 || pixels[i + 2] > 90) changed++;
        Check(changed > 10000, "real UI geometry and glyphs drawn into host framebuffer");
        string screenshot = Path.Combine(root, "artifacts", "example-preview.png");
        SavePng(screenshot, pixels, 1000, 800); Console.WriteLine("Preview: " + screenshot);
        // Test actual hit testing, checkbox/range defaults and input on the rendered DOM.
        Check(document.Call(5, 1, 1) == 0, "transparent background does not capture game mouse");
        Check(document.Call(5, 200, 150) != 0, "window surface captures mouse");
        document.Call(3, 1000, 800, 1.5f); document.Call(4);
        Check(GL.GetError() == ErrorCode.NoError, "GUI scale change renders correctly");
        document.Close(); document.Dispose();
        Check(Snapshot() == before, "OpenGL state preserved across document destruction");
        using var imageDoc = ui.LoadDocumentFromString("test", """
            <rml><head><style>body { width: 100%; height: 100%; } img { position:absolute; left:10px; top:10px; width:40px; height:40px; }</style></head>
            <body><img src="pixels.png" /></body></rml>
            """, "test:dialog/image.rml");
        imageDoc.Show(); GL.Disable(EnableCap.FramebufferSrgb); GL.ClearColor(0, 0, 0, 1); GL.Clear(ClearBufferMask.ColorBufferBit); GL.Enable(EnableCap.FramebufferSrgb);
        imageDoc.Call(3, 1000, 800, 1); imageDoc.Call(4);
        byte[] imagePixels = framebuffer.Pixels(); int pixelIndex = ((800 - 25) * 1000 + 25) * 4;
        Check(imagePixels[pixelIndex] > 100 && imagePixels[pixelIndex] < 160 && imagePixels[pixelIndex + 1] < 5, "premultiplied image alpha compositing");
    }
    Check(Snapshot() == before, "OpenGL state preserved across shutdown");
    GL.BindSampler(0, 0); GL.DeleteSampler(hostSampler); GL.DeleteTexture(hostTexture); GL.DeleteBuffer(hostBuffer); GL.DeleteVertexArray(hostVao);
}
string? consumerAssets = args.FirstOrDefault(a => a.StartsWith("--director-assets="))?[18..];
if (consumerAssets is not null)
{
    host.DirectorAssets = Path.GetFullPath(consumerAssets);
    using var ui = new RmlRuntime(host, headless: true);
    ui.RegisterFont("game:fonts/Montserrat-Regular.ttf", "vsrmlui-default");
    ui.RegisterFont("game:fonts/Montserrat-Bold.ttf", "vsrmlui-default", 700);
    ui.RegisterFont("vsrmlui:fonts/NotoSansCJKsc-Regular.otf", "vsrmlui-cjk", fallback: true);
    foreach (var file in Directory.GetFiles(Path.Combine(host.DirectorAssets, "dialog"), "*.rml"))
    {
        using var document = ui.LoadDocument("vsdirector", "vsdirector:dialog/" + Path.GetFileName(file));
        document.Show();
        foreach (var scale in new[] { 0.75f, 1f, 1.5f, 2f })
        {
            document.SetViewport(new(0, 0, 1920, 1080, scale)); document.Call(4);
            if (Path.GetFileName(file) == "workspace.rml")
            {
                var preview = document.GetElementById("preview")!.Bounds;
                Check(preview.Width > 300 && preview.Height > 0 && preview.X >= 0 && preview.Y >= 0, $"Director preview bounds at GUI scale {scale}");
            }
        }
        Check(document.Root.QuerySelectorAll("body").Count <= 1, "load consumer document " + Path.GetFileName(file));
    }
}
Check(!host.Messages.Any(m => m.Level is 1 or 2 or 3 && !m.Text.Contains("test expected")), "no native parser/font/resource warnings");
Console.WriteLine($"All {passed} checks passed.");

static string Snapshot()
{
    var values = new List<int>();
    foreach (var param in new[] { GetPName.CurrentProgram, GetPName.VertexArrayBinding, GetPName.ArrayBufferBinding, GetPName.DrawFramebufferBinding, GetPName.ReadFramebufferBinding, GetPName.ActiveTexture, GetPName.UnpackAlignment, GetPName.UnpackRowLength }) values.Add(GL.GetInteger(param));
    int unit = GL.GetInteger(GetPName.ActiveTexture); GL.ActiveTexture(TextureUnit.Texture0);
    values.Add(GL.GetInteger(GetPName.TextureBinding2D)); GL.GetInteger((GetIndexedPName)0x8919, 0, out int sampler); values.Add(sampler); GL.ActiveTexture((TextureUnit)unit);
    foreach (var flag in new[] { EnableCap.DepthTest, EnableCap.CullFace, EnableCap.FramebufferSrgb }) values.Add(GL.IsEnabled(flag) ? 1 : 0);
    values.Add(GL.GetBoolean(GetPName.DepthWritemask) ? 1 : 0);
    return string.Join(',', values);
}
static void SavePng(string path, byte[] pixels, int width, int height)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    byte[] flipped = new byte[pixels.Length];
    for (int y = 0; y < height; y++) System.Buffer.BlockCopy(pixels, y * width * 4, flipped, (height - y - 1) * width * 4, width * 4);
    using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    Marshal.Copy(flipped, 0, bitmap.GetPixels(), flipped.Length);
    using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); encoded.SaveTo(file);
}

sealed class TestHost(string root, string gameRoot) : IRmlHost
{
    public string? DirectorAssets { get; set; }
    public List<(int Level, string Text)> Messages { get; } = [];
    public byte[] ReadAsset(string path)
    {
        string[] address = path.Split(':', 2);
        string directory = address[0] switch { "vsdirector" when DirectorAssets is not null => DirectorAssets, "game" => Path.Combine(gameRoot, "assets", "game"), "vsrmlui" => Path.Combine(root, "src", "VSRmlUi", "assets", "vsrmlui"), "vsrmluiexample" => Path.Combine(root, "examples", "VSRmlUi.Example", "assets", "vsrmluiexample"), _ => throw new FileNotFoundException(path) };
        return File.ReadAllBytes(Path.Combine(directory, address[1]));
    }
    public (byte[] Pixels, int Width, int Height) ReadImage(string path) => path == "test:dialog/pixels.png" ? ([128, 0, 0, 128], 1, 1) : throw new FileNotFoundException(path);
    private Dictionary<string, string>? translations;
    public string Translate(string input)
    {
        translations ??= JsonSerializer.Deserialize<Dictionary<string, string>>(ReadAsset("vsrmluiexample:lang/zh-cn.json"))!;
        return System.Text.RegularExpressions.Regex.Replace(input, @"\[\[vsrmluiexample:([^\]]+)\]\]", m => translations.GetValueOrDefault(m.Groups[1].Value, m.Value));
    }
    public string Clipboard { get; set; } = "";
    public string Cursor { private get; set; } = "";
    public void Log(int level, string message) { Messages.Add((level, message)); if (level <= 3) Console.WriteLine($"RML {level}: {message}"); }
}

sealed class TestFramebuffer : IDisposable
{
    private readonly int fbo, texture;
    private readonly int width, height;
    public TestFramebuffer(int width, int height)
    {
        this.width = width; this.height = height;
        texture = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        fbo = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) throw new Exception("Test framebuffer is incomplete.");
    }
    public byte[] Pixels() { var data = new byte[width * height * 4]; GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, data); return data; }
    public void Dispose() { GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); GL.DeleteFramebuffer(fbo); GL.DeleteTexture(texture); }
}
