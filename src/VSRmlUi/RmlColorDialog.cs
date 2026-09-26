using System.Globalization;
using System.Text;
using Vintagestory.API.Client;

namespace VSRmlUi;

/// <summary>Persistent custom slots shared by the client color dialogs.</summary>
public sealed class RmlCustomColorPalette
{
    public string[] Colors { get; set; } = Enumerable.Repeat("#FFFFFF", 16).ToArray();
    public int NextSlot { get; set; }
    internal void Normalize()
    {
        Colors = Enumerable.Range(0, 16).Select(i => Colors is not null && i < Colors.Length && RmlControls.TryParseColor(Colors[i], out uint value) ? RmlColorDialog.Hex(value) : "#FFFFFF").ToArray();
        NextSlot = Math.Clamp(NextSlot, 0, 15);
    }
}

/// <summary>A classic, fully RmlUi color dialog. Only OK publishes a color; closing cancels the draft.</summary>
public sealed class RmlColorDialog : IDisposable
{
    private static readonly string[] BasicColors =
    [
        "#FF8080", "#FFFF80", "#80FF80", "#00FF80", "#80FFFF", "#0080FF", "#FF80C0", "#FF80FF",
        "#FF0000", "#FFFF00", "#80FF00", "#00FF40", "#00FFFF", "#0080C0", "#8080C0", "#FF00FF",
        "#804040", "#FF8040", "#00FF00", "#008080", "#004080", "#8080FF", "#800040", "#FF0080",
        "#800000", "#FF8000", "#008000", "#008040", "#0000FF", "#0000A0", "#800080", "#8000FF",
        "#400000", "#804000", "#004000", "#004040", "#000080", "#000040", "#400040", "#400080",
        "#000000", "#808000", "#808040", "#808080", "#408080", "#C0C0C0", "#400040", "#FFFFFF"
    ];
    private readonly RmlDocument parent;
    private readonly Action<string> accepted;
    private readonly bool allowAlpha;
    private readonly Dictionary<string, string> displayed = new();
    private uint rgba;
    private double hue, saturation, lightness;
    private string? dragging;
    private bool disposed;
    private int customSlot;
    public RmlDocument Document { get; }

    public static RmlColorDialog Show(RmlDocument parent, string value, Action<string> accepted, bool allowAlpha = true)
    {
        ArgumentNullException.ThrowIfNull(parent); ArgumentNullException.ThrowIfNull(accepted);
        parent.EnsureAlive();
        return new(parent, value, accepted, allowAlpha);
    }

    private RmlColorDialog(RmlDocument parent, string value, Action<string> accepted, bool allowAlpha)
    {
        this.parent = parent; this.accepted = accepted; this.allowAlpha = allowAlpha;
        if (!RmlControls.TryParseColor(value, out rgba)) rgba = 0xffffffff;
        if (!allowAlpha) rgba |= 255;
        ToHsl(rgba);
        parent.Runtime.ColorPalette.Normalize();
        customSlot = parent.Runtime.ColorPalette.NextSlot;
        Document = parent.Runtime.LoadDocumentFromString(parent.OwnerModId, Markup(), "vsrmlui:dialog/color-dialog.rml",
            new RmlDocumentOptions { Mode = RmlWindowMode.Modal, DrawOrder = Math.Max(.95, parent.Options.DrawOrder + .01), InputOrder = Math.Min(-.3, parent.Options.InputOrder - .1), CloseOnEscape = true });
        foreach (string variant in new[] { "night", "day", "contrast" })
        {
            string name = "vs-theme-" + variant;
            Get("color-dialog").SetClass(name, parent.Root.ClassNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name) || parent.QuerySelector("." + name) is not null);
        }
        Document.Closed += Dispose;
        parent.Closed += Dispose;
        Document.InputCancelled += EndDrag;
        Document.InputFilter = Filter;
        Get("cancel").On("click", _ => Dispose());
        Get("ok").On("click", _ =>
        {
            if (Document.QuerySelector("input[aria-invalid=true]") is not null) return;
            string result = Hex(rgba); Dispose();
            if (!parent.IsDisposed) accepted(result);
        });
        Get("add-custom").On("click", _ => SaveCustom());
        for (int i = 0; i < BasicColors.Length; i++)
        {
            string color = BasicColors[i];
            Get("basic-" + i).On("click", _ => Pick(color));
        }
        for (int i = 0; i < 16; i++)
        {
            int slot = i;
            Get("custom-" + i).On("click", _ => { customSlot = slot; Pick(parent.Runtime.ColorPalette.Colors[slot]); });
        }
        foreach (string channel in new[] { "R", "G", "B", "H", "S", "L", "hex", "A" })
        {
            if (!allowAlpha && channel == "A") continue;
            string name = channel;
            Get(name).On("change", _ => Edit(name));
        }
        foreach (string surface in new[] { "spectrum", "luminance" })
        {
            string name = surface;
            Get(name).On("mousedown", e =>
            {
                if (e.Button != 0) return;
                dragging = name; Get(name).CapturePointer(); Get(name).Focus();
                Move(e.MouseX, e.MouseY);
            });
            Get(name).On("keydown", e => Keyboard(name, e.Key));
        }
        Get("old-color").SetProperty("background-color", Hex(rgba));
        Sync(); Document.Show();
    }

    private RmlElement Get(string id) => Document.GetElementById(id)!;
    internal static string Hex(uint value) => "#" + value.ToString("X8")[..((value & 255) == 255 ? 6 : 8)];
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Label(string key) => "[[vsrmlui:color-" + key + "]]";

    private void Pick(string value)
    {
        if (!RmlControls.TryParseColor(value, out rgba)) return;
        if (!allowAlpha) rgba |= 255;
        ToHsl(rgba); Sync();
    }
    private void Edit(string name)
    {
        string text = Get(name).Value;
        if (displayed.GetValueOrDefault(name) == text) return;
        if (name == "A" && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double alpha) && alpha == (rgba & 255)) return;
        if (name == "hex")
        {
            if (!RmlControls.TryParseColor(text, out uint color) || (!allowAlpha && (color & 255) != 255)) { Invalid(name); return; }
            rgba = color; ToHsl(rgba);
        }
        else
        {
            double max = name == "H" ? 360 : name is "S" or "L" ? 100 : 255;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < 0 || value > max
                || (name is "R" or "G" or "B" or "A" && value != Math.Truncate(value))) { Invalid(name); return; }
            if (name is "R" or "G" or "B" or "A")
            {
                int shift = name switch { "R" => 24, "G" => 16, "B" => 8, _ => 0 };
                rgba = (rgba & ~(255u << shift)) | ((uint)value << shift);
                if (name != "A") ToHsl(rgba);
            }
            else
            {
                if (name == "H") hue = value; else if (name == "S") saturation = value / 100; else lightness = value / 100;
                rgba = FromHsl(hue, saturation, lightness, (byte)rgba);
            }
        }
        Sync();
    }
    private void Invalid(string name)
    {
        Get(name).SetAttribute("aria-invalid", "true");
        Get("ok").SetAttribute("disabled", "disabled");
        Get("message").InnerRml = Label("invalid");
    }
    private void Write(string id, string text)
    {
        displayed[id] = text;
        var element = Get(id);
        element.RemoveAttribute("aria-invalid");
        if (element.Value != text) element.Value = text;
    }
    private void Sync()
    {
        Write("R", N(rgba >> 24)); Write("G", N((rgba >> 16) & 255)); Write("B", N((rgba >> 8) & 255));
        Write("H", N(hue)); Write("S", N(saturation * 100)); Write("L", N(lightness * 100)); Write("hex", Hex(rgba));
        if (allowAlpha) Write("A", N(rgba & 255));
        Get("ok").RemoveAttribute("disabled"); Get("message").Text = "";
        Get("new-color").SetProperty("background-color", Hex(rgba));
        Get("crosshair").SetProperty("left", N(hue / 360 * 100) + "%");
        Get("crosshair").SetProperty("top", N((1 - saturation) * 100) + "%");
        Get("light-marker").SetProperty("top", N((1 - lightness) * 100) + "%");
        string middle = Hex(FromHsl(hue, saturation, .5));
        Get("light-top").SetProperty("decorator", $"vertical-gradient(#ffffff {middle})");
        Get("light-bottom").SetProperty("decorator", $"vertical-gradient({middle} #000000)");
        Get("spectrum").SetAttribute("aria-valuetext", $"H {N(hue)}, S {N(saturation * 100)}");
        Get("luminance").SetAttribute("aria-valuenow", N(lightness * 100));
        for (int i = 0; i < 16; i++) Get("custom-" + i).SetClass("current-slot", i == customSlot);
    }
    private void SaveCustom()
    {
        var palette = parent.Runtime.ColorPalette;
        string previous = palette.Colors[customSlot]; int previousNext = palette.NextSlot;
        palette.Colors[customSlot] = Hex(rgba); palette.NextSlot = (customSlot + 1) % 16;
        try { parent.Runtime.SaveColorPalette?.Invoke(); }
        catch
        {
            palette.Colors[customSlot] = previous; palette.NextSlot = previousNext;
            Get("message").InnerRml = Label("save-failed"); return;
        }
        Get("custom-" + customSlot).SetProperty("background-color", Hex(rgba));
        Get("custom-" + customSlot).SetAttribute("title", Hex(rgba));
        customSlot = palette.NextSlot; Sync();
    }
    private bool Filter(RmlInputEvent input)
    {
        if (input.Kind == RmlInputKind.KeyDown && input.Key == (int)GlKeys.Escape) { Dispose(); return true; }
        if (dragging is null) return false;
        if (input.Kind == RmlInputKind.MouseMove && Document.Host is GameDialog view && !view.PrimaryButtonDown) { EndDrag(); return false; }
        if (input.Kind == RmlInputKind.MouseMove) { Move(input.X - Document.Viewport.X, input.Y - Document.Viewport.Y); return true; }
        if (input.Kind == RmlInputKind.MouseUp && input.Button == 0) { EndDrag(); return false; }
        return false;
    }
    private void Move(float x, float y)
    {
        if (dragging is null) return;
        var bounds = Get(dragging).Bounds;
        double vertical = Math.Clamp((y - bounds.Y) / Math.Max(1, bounds.Height - 1), 0, 1);
        if (dragging == "spectrum")
        {
            hue = Math.Clamp((x - bounds.X) / Math.Max(1, bounds.Width - 1), 0, 1) * 360;
            saturation = 1 - vertical;
        }
        else lightness = 1 - vertical;
        rgba = FromHsl(hue, saturation, lightness, (byte)rgba); Sync();
    }
    private void Keyboard(string name, int key)
    {
        // RmlUi key identifiers, as reported by the native keydown event.
        if (key is not (90 or 91 or 92 or 93)) return;
        if (name == "luminance") lightness = Math.Clamp(lightness + (key is 91 or 92 ? .01 : -.01), 0, 1);
        else if (key is 90 or 92) hue = (hue + (key == 92 ? 1 : -1) + 360) % 360;
        else saturation = Math.Clamp(saturation + (key == 91 ? .01 : -.01), 0, 1);
        rgba = FromHsl(hue, saturation, lightness, (byte)rgba); Sync();
    }
    private void EndDrag() { dragging = null; if (!Document.IsDisposed) Document.ReleasePointer(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; parent.Closed -= Dispose;
        Document.Dispose();
        if (!parent.IsDisposed && parent.IsVisible && parent.Host is GameDialog view) view.RestoreFocus();
    }

    private void ToHsl(uint value)
    {
        double r = (value >> 24) / 255d, g = ((value >> 16) & 255) / 255d, b = ((value >> 8) & 255) / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        lightness = (max + min) / 2;
        saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        // Keep hue on achromatic edits so adjusting saturation restores the chosen hue.
        if (delta > 0) hue = ((max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60 + 360) % 360;
    }
    internal static uint FromHsl(double h, double s, double l, byte alpha = 255)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        uint Channel(double v) => (uint)Math.Clamp(Math.Round((v + m) * 255), 0, 255);
        return (Channel(r) << 24) | (Channel(g) << 16) | (Channel(b) << 8) | alpha;
    }

    private string Markup()
    {
        string Checkers() => string.Concat(Enumerable.Range(0, 32).Select(i => $"<span class='checker' style='left:{N(i % 8 * 12.5)}%;top:{i / 8 * 25}%;background-color:{((i + i / 8) % 2 == 0 ? "#bbbbbb" : "#777777")}'/>"));
        string Preview(string name) => $"<div class='preview-item'><div class='caption'>{Label(name)}</div><div class='preview'>{Checkers()}<div id='{name}-color' class='preview-color'/></div></div>";
        string Field(string name, string label) => $"<div class='component'><label for='{name}'>{label}</label><input id='{name}' type='text' inputmode='decimal'/></div>";
        var html = new StringBuilder("<rml><head><link type='text/rcss' href='vsrmlui:dialog/controls.rcss'/><link type='text/rcss' href='vsrmlui:dialog/color-dialog.rcss'/><link type='text/rcss' href='vsrmlui:dialog/dialog-theme.rcss'/></head><body>");
        html.Append($"<div id='color-dialog' role='dialog' aria-modal='true'><div class='color-header'>{Label("title")}</div><div class='color-content'><div class='palette-column'><div class='caption'>{Label("basic")}</div><div class='palette'>");
        for (int i = 0; i < BasicColors.Length; i++) html.Append(Swatch("basic-" + i, BasicColors[i]));
        html.Append($"</div><div class='caption custom-title'>{Label("custom")}</div><div class='palette'>");
        for (int i = 0; i < 16; i++) html.Append(Swatch("custom-" + i, parent.Runtime.ColorPalette.Colors[i]));
        html.Append($"</div><button id='add-custom'>{Label("add")}</button><div class='previews'>{Preview("old")}{Preview("new")}</div></div><div class='editor-column'><div class='caption'>{Label("spectrum")}</div><div class='spectrum-row'><div id='spectrum' tab-index='auto' role='slider' aria-label='{Label("spectrum")}'>");
        string[] colors = ["#ff0000", "#ffff00", "#00ff00", "#00ffff", "#0000ff", "#ff00ff", "#ff0000"];
        for (int i = 0; i < 6; i++) html.Append($"<div class='hue-strip' style='left:{N(i * 100d / 6)}%;decorator:horizontal-gradient({colors[i]} {colors[i + 1]})'/>");
        html.Append($"<div class='saturation-overlay'/><div id='crosshair'><div class='cross-inner'/></div></div><div id='luminance' tab-index='auto' role='slider' aria-label='{Label("lightness")}' aria-valuemin='0' aria-valuemax='100'><div id='light-top'/><div id='light-bottom'/><div id='light-marker'/></div></div>");
        html.Append($"<div class='components'>{Field("H", "H / deg")}{Field("S", "S / %")}{Field("L", "L / %")}</div><div class='components'>{Field("R", "R")}{Field("G", "G")}{Field("B", "B")}</div><div class='hex-row'><label for='hex'>HEX</label><input id='hex' type='text' maxlength='9'/></div>");
        if (allowAlpha) html.Append($"<div class='alpha-row'><label for='A'>{Label("alpha")}</label>{RmlControls.Slider("A", rgba & 255, 0, 255)}</div>");
        html.Append($"</div></div><div class='color-footer'><div id='message'/><button id='cancel'>{Label("cancel")}</button><button id='ok' class='primary'>{Label("ok")}</button></div></div></body></rml>");
        return html.ToString();
    }
    private static string Swatch(string id, string color) => $"<div class='swatch-cell'><button id='{id}' class='palette-swatch' style='background-color:{color}' title='{color}' aria-label='{color}'/></div>";
}
