using System.Globalization;

namespace VSRmlUi;

/// <summary>Reusable controls. Link dialog/controls.rcss and bind pickers after loading their document.</summary>
public static class RmlControls
{
    public static string Slider(string id, double value, double min, double max, double step = 1)
        => $"<input id='{E(id)}' class='range' type='range' min='{N(min)}' max='{N(max)}' step='{N(step)}' value='{N(value)}'/>";

    /// <summary>Creates the alternate slider preset with its value overlaid in the slot.</summary>
    public static string SliderWithValue(string id, double value, double min, double max, double step = 1)
        => $"<div class='vs-slider-value'>{Slider(id, value, min, max, step)}<span id='{E(id)}-value' class='vs-slider-value-text'>{N(value)}</span></div>";

    /// <summary>Keeps a slider preset's centered value label synchronized with its change events.</summary>
    public static IDisposable BindSliderValue(RmlDocument document, string id)
    {
        var slider = document.GetElementById(id) ?? throw new ArgumentException($"Slider '{id}' was not found.", nameof(id));
        var display = document.GetElementById(id + "-value") ?? throw new ArgumentException($"Slider value label for '{id}' was not found.", nameof(id));
        display.Text = slider.Value;
        return slider.On("change", e => display.Text = string.IsNullOrEmpty(e.Value) ? slider.Value : e.Value);
    }

    public static string AudioBars(string id, int count = 24)
    {
        if (count is < 3 or > 64) throw new ArgumentOutOfRangeException(nameof(count));
        return $"<div id='{E(id)}' class='vs-audio-bars' role='meter' aria-valuemin='0' aria-valuemax='1' aria-valuenow='0'>"
            + string.Concat(Enumerable.Range(0, count).Select(i => $"<span id='{E(id)}-bar-{i}' class='vs-audio-bar' style='height:3dp'/>") ) + "</div>";
    }

    public static void SetAudioBars(RmlDocument document, string id, double volume, double phase = 0)
    {
        if (!double.IsFinite(volume) || !double.IsFinite(phase)) throw new ArgumentOutOfRangeException(nameof(volume));
        var root = document.GetElementById(id) ?? throw new ArgumentException($"Audio bars '{id}' were not found.", nameof(id));
        root.SetAttribute("aria-valuenow", N(Math.Clamp(volume, 0, 1)));
        double level = Math.Clamp(volume, 0, 1);
        for (int i = 0; ; i++)
        {
            var bar = document.GetElementById(id + "-bar-" + i);
            if (bar is null) break;
            double wave = 0.35 + 0.65 * Math.Abs(Math.Sin(i * 1.67 + phase) * Math.Cos(i * 0.39 - phase * 0.7));
            bar.SetProperty("height", N(3 + 45 * level * wave) + "dp");
        }
    }

    public static string PitchCurve(string id)
        => $"<svg id='{E(id)}' class='vs-pitch-curve' width='480' height='120' viewBox='0 0 480 120'><path d='M0 60 L480 60' fill='none' stroke='#2a9bff' stroke-width='3'/></svg>";

    public static void SetPitchCurve(RmlDocument document, string id, double pitchHz, double amplitude, double phase = 0)
    {
        if (!double.IsFinite(pitchHz) || !double.IsFinite(amplitude) || !double.IsFinite(phase)) throw new ArgumentOutOfRangeException(nameof(pitchHz));
        var curve = document.GetElementById(id) ?? throw new ArgumentException($"Pitch curve '{id}' was not found.", nameof(id));
        double cycles = Math.Clamp(pitchHz / 80, 0.5, 16);
        double height = 49 * Math.Clamp(amplitude, 0, 1);
        var points = new System.Text.StringBuilder();
        for (int i = 0; i <= 96; i++)
        {
            double x = i * 5;
            double envelope = Math.Sin(Math.PI * i / 96);
            double y = 60 - height * envelope * Math.Sin(2 * Math.PI * cycles * i / 96 + phase);
            points.Append(i == 0 ? "M" : " L").Append(N(x)).Append(' ').Append(N(y));
        }
        curve.InnerRml = $"<path d='{points}' fill='none' stroke='#2a9bff' stroke-width='3'/>";
        curve.SetAttribute("aria-label", N(pitchHz) + " Hz");
    }

    public static string ColorPicker(string id, string value)
    {
        if (!TryParseColor(value, out uint rgba)) rgba = 0xffffffff;
        string hex = "#" + rgba.ToString("X8")[..(value.Trim().Length == 9 ? 8 : 6)];
        return $"<div class='rml-picker color-picker'><div class='picker-entry'><button id='{E(id)}-toggle' class='color-swatch' style='background-color:{hex}' aria-label='[[vsrmlui:color-title]]' aria-expanded='false'></button><input id='{E(id)}' type='text' class='field color-value' value='{hex}'/></div></div>";
    }

    public static string TimePicker(string id, TimeSpan value, bool includeSeconds = true)
    {
        string format = includeSeconds ? "hh\\:mm\\:ss" : "hh\\:mm";
        string placeholder = includeSeconds ? "HH:MM:SS" : "HH:MM";
        return $"<div class='rml-picker time-picker'><input id='{E(id)}' class='field time-value' type='text' inputmode='numeric' value='{E(value.ToString(format, CultureInfo.InvariantCulture))}' placeholder='{placeholder}'/>"
            + Channel(id, "HH", value.Hours, 23) + Channel(id, "MM", value.Minutes, 59)
            + (includeSeconds ? Channel(id, "SS", value.Seconds, 59) : "") + "</div>";
    }

    private static string Channel(string id, string channel, double value, double max)
        => $"<div class='picker-channel'><label for='{E(id)}-{channel}'>{channel}</label>{Slider(id + "-" + channel, value, 0, max)}</div>";

    public static void BindColorPicker(RmlDocument document, string id, Action<string> changed, Action<RmlDocument>? dialogOpened = null, bool allowAlpha = true)
    {
        var input = document.GetElementById(id)!;
        var swatch = document.GetElementById(id + "-toggle")!;
        RmlColorDialog? dialog = null;
        string last = input.Value;
        swatch.On("click", _ =>
        {
            if (dialog is { Document.IsDisposed: false }) return;
            dialog = RmlColorDialog.Show(document, last, hex => Publish(hex), allowAlpha);
            swatch.SetAttribute("aria-expanded", "true");
            dialog.Document.Closed += () => { if (!document.IsDisposed) swatch.SetAttribute("aria-expanded", "false"); };
            dialogOpened?.Invoke(dialog.Document);
        });
        void Publish(string hex)
        {
            SetValue(input, hex);
            swatch.SetProperty("background-color", hex);
            input.RemoveAttribute("aria-invalid");
            if (hex == last) return;
            last = hex;
            changed(hex);
        }
        input.On("change", _ =>
        {
            if (TryParseColor(input.Value, out uint rgba)) Publish(RmlColorDialog.Hex(allowAlpha ? rgba : rgba | 255));
            else input.SetAttribute("aria-invalid", "true");
        });
    }

    public static void BindTimePicker(RmlDocument document, string id, Action<TimeSpan> changed, bool includeSeconds = true)
    {
        var input = document.GetElementById(id)!;
        var hours = document.GetElementById(id + "-HH")!;
        var minutes = document.GetElementById(id + "-MM")!;
        var seconds = includeSeconds ? document.GetElementById(id + "-SS") : null;
        TryParseTime(input.Value, out TimeSpan last);
        void Publish(TimeSpan value)
        {
            if (!includeSeconds) value = TimeSpan.FromMinutes((int)value.TotalMinutes);
            SetValue(input, value.ToString(includeSeconds ? "hh\\:mm\\:ss" : "hh\\:mm", CultureInfo.InvariantCulture));
            input.RemoveAttribute("aria-invalid");
            SetValue(hours, N(value.Hours)); SetValue(minutes, N(value.Minutes));
            if (seconds is not null) SetValue(seconds, N(value.Seconds));
            if (last == value) return;
            last = value; changed(value);
        }
        input.On("change", _ =>
        {
            if (TryParseTime(input.Value, out var value)) Publish(value);
            else input.SetAttribute("aria-invalid", "true");
        });
        foreach (var channel in new[] { hours, minutes, seconds }.OfType<RmlElement>())
            channel.On("change", _ => Publish(new TimeSpan((int)Math.Clamp(Parse(hours.Value), 0, 23), (int)Math.Clamp(Parse(minutes.Value), 0, 59), seconds is null ? 0 : (int)Math.Clamp(Parse(seconds.Value), 0, 59))));
    }

    private static double Parse(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : 0;
    private static void SetValue(RmlElement element, string value) { if (element.Value != value) element.Value = value; }

    public static bool TryParseTime(string? text, out TimeSpan value)
        => TimeSpan.TryParseExact(text?.Trim(), ["hh\\:mm\\:ss", "hh\\:mm"], CultureInfo.InvariantCulture, out value)
           && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);

    public static bool TryParseColor(string? text, out uint rgba)
    {
        rgba = 0;
        string s = (text ?? string.Empty).Trim().TrimStart('#');
        if (s.Length is not (6 or 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed)) return false;
        rgba = s.Length == 6 ? (parsed << 8) | 0xffu : parsed;
        return true;
    }

    private static string E(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
    private static string N(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);
}
