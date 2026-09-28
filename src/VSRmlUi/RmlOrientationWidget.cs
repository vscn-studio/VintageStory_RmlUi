using System.Globalization;
using System.Numerics;
using System.Text;

namespace VSRmlUi;

/// <summary>Projected 3D axis gizmo with pointer-driven yaw and pitch.</summary>
public sealed class RmlOrientationWidget(string id)
{
    private readonly List<IDisposable> handlers = [];
    private int dragX, dragY;
    private float startYaw, startPitch;
    public float Yaw { get; private set; } = -0.5f;
    public float Pitch { get; private set; } = 0.35f;
    public string Markup() => $"<div id='{E(id)}' class='vs-orientation' role='img' aria-label='3D orientation'><svg id='{E(id)}-axes' width='180' height='180' viewBox='0 0 180 180'/></div>";

    public void Set(RmlDocument document, float yaw, float pitch)
    {
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch)) throw new ArgumentOutOfRangeException(nameof(yaw));
        Yaw = yaw; Pitch = Math.Clamp(pitch, -1.45f, 1.45f);
        var rotation = Quaternion.CreateFromYawPitchRoll(Yaw, Pitch, 0);
        var axes = new[] { (Vector3.UnitX, "X", "#f07770"), (Vector3.UnitY, "Y", "#78c96b"), (Vector3.UnitZ, "Z", "#67a9f4") };
        var svg = new StringBuilder("<circle cx='90' cy='90' r='75' fill='#1c2525' stroke='#536769' stroke-width='2'/>");
        foreach (var (axis, label, color) in axes.OrderBy(item => Vector3.Transform(item.Item1, rotation).Z))
        {
            var vector = Vector3.Transform(axis, rotation);
            float x = 90 + vector.X * 55, y = 90 - vector.Y * 55;
            svg.Append($"<path d='M90 90 L{N(x)} {N(y)}' stroke='{color}' stroke-width='5' stroke-linecap='round'/>");
            svg.Append($"<circle cx='{N(x)}' cy='{N(y)}' r='12' fill='{color}'/><text x='{N(x)}' y='{N(y + 5)}' text-anchor='middle' fill='#111111' font-size='14' font-weight='bold'>{label}</text>");
        }
        document.GetElementById(id + "-axes")!.InnerRml = svg.ToString();
        document.GetElementById(id)!.SetAttribute("aria-label", $"3D orientation, yaw {N(Yaw)}, pitch {N(Pitch)}");
    }

    public void Bind(RmlDocument document)
    {
        var element = document.GetElementById(id) ?? throw new ArgumentException("Orientation widget not found.", nameof(document));
        Set(document, Yaw, Pitch);
        handlers.Add(element.On("mousedown", e => { dragX = e.MouseX; dragY = e.MouseY; startYaw = Yaw; startPitch = Pitch; }));
        handlers.Add(element.On("drag", e => Set(document, startYaw + (e.MouseX - dragX) * 0.012f, startPitch - (e.MouseY - dragY) * 0.012f)));
    }

    public void Unbind() { foreach (var handler in handlers) handler.Dispose(); handlers.Clear(); }
    private static string E(string text) => System.Net.WebUtility.HtmlEncode(text);
    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
