using System.Globalization;

namespace VSRmlUi;

public static class RmlKnob
{
    public static string Markup(string id, double value = 0.5)
        => $"<div id='{E(id)}' class='vs-knob' role='slider' tab-index='auto' aria-valuemin='0' aria-valuemax='100' aria-valuenow='{N(Math.Clamp(value, 0, 1) * 100)}'><svg id='{E(id)}-ring' width='80' height='80' viewBox='0 0 80 80'/><span id='{E(id)}-value' class='vs-knob-value'>{N(Math.Clamp(value, 0, 1) * 100)}</span></div>";

    public static void SetValue(RmlDocument document, string id, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        value = Math.Clamp(value, 0, 1);
        var element = document.GetElementById(id) ?? throw new ArgumentException("Knob not found.", nameof(id));
        element.SetAttribute("aria-valuenow", N(value * 100));
        document.GetElementById(id + "-value")!.Text = N(value * 100);
        double angle = (-130 + 260 * value) * Math.PI / 180;
        double endX = 40 + 30 * Math.Sin(angle), endY = 40 - 30 * Math.Cos(angle);
        string sweep = value > 0.5 ? "1" : "0";
        document.GetElementById(id + "-ring")!.InnerRml =
            "<path d='M17.02 59.28 A30 30 0 1 1 62.98 59.28' fill='none' stroke='#4a5552' stroke-width='6' stroke-linecap='round'/>"
            + $"<path d='M17.02 59.28 A30 30 0 {sweep} 1 {N(endX)} {N(endY)}' fill='none' stroke='#39bfa6' stroke-width='6' stroke-linecap='round'/>"
            + $"<circle cx='{N(endX)}' cy='{N(endY)}' r='4' fill='#ffffff'/>";
    }

    public static IDisposable Bind(RmlDocument document, string id, Action<double> changed)
    {
        var element = document.GetElementById(id) ?? throw new ArgumentException("Knob not found.", nameof(id));
        SetValue(document, id, double.TryParse(element.GetAttribute("aria-valuenow"), NumberStyles.Float, CultureInfo.InvariantCulture, out double initial) ? initial / 100 : 0);
        int startY = 0; double startValue = 0;
        var handlers = new List<IDisposable>
        {
            element.On("mousedown", e => { startY = e.MouseY; startValue = Current(); }),
            element.On("drag", e => Publish(startValue + (startY - e.MouseY) / 120.0)),
            element.On("mousewheel", e => Publish(Current() + (e.Wheel > 0 ? 0.02 : -0.02))),
            element.On("keydown", e => { if (e.Key is 90 or 92) Publish(Current() + (e.Key == 92 ? 0.01 : -0.01)); })
        };
        return new Bindings(handlers);
        double Current() => double.Parse(element.GetAttribute("aria-valuenow"), CultureInfo.InvariantCulture) / 100;
        void Publish(double value) { value = Math.Clamp(value, 0, 1); SetValue(document, id, value); changed(value); }
    }

    private sealed class Bindings(List<IDisposable> handlers) : IDisposable
    { public void Dispose() { foreach (var handler in handlers) handler.Dispose(); } }

    private static string E(string value) => System.Net.WebUtility.HtmlEncode(value);
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
