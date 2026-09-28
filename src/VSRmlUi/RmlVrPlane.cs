using System.Numerics;

namespace VSRmlUi;

public readonly record struct RmlVrRay(Vector3 Origin, Vector3 Direction);
public readonly record struct RmlVrHit(float Distance, float U, float V, int PixelX, int PixelY);

/// <summary>Maps controller rays onto a host-rendered RmlUi plane in world space.</summary>
public sealed class RmlVrPlane
{
    public Vector3 Center { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public float WidthMeters { get; set; } = 1.2f;
    public float HeightMeters { get; set; } = 0.7f;
    public int TextureWidth { get; set; } = 1200;
    public int TextureHeight { get; set; } = 700;

    public bool TryHit(RmlVrRay ray, out RmlVrHit hit)
    {
        hit = default;
        if (!Finite(Center) || !Finite(ray.Origin) || !Finite(ray.Direction)
            || !float.IsFinite(Rotation.X) || !float.IsFinite(Rotation.Y) || !float.IsFinite(Rotation.Z) || !float.IsFinite(Rotation.W)
            || WidthMeters <= 0 || HeightMeters <= 0 || TextureWidth <= 0 || TextureHeight <= 0) return false;
        var inverse = Quaternion.Inverse(Rotation);
        var localOrigin = Vector3.Transform(ray.Origin - Center, inverse);
        var localDirection = Vector3.Transform(ray.Direction, inverse);
        if (Math.Abs(localDirection.Z) < 1e-6f) return false;
        float distance = -localOrigin.Z / localDirection.Z;
        if (distance < 0 || !float.IsFinite(distance)) return false;
        var point = localOrigin + localDirection * distance;
        float u = point.X / WidthMeters + 0.5f;
        float v = 0.5f - point.Y / HeightMeters;
        if (u is < 0 or > 1 || v is < 0 or > 1) return false;
        hit = new(distance, u, v, Math.Clamp((int)(u * TextureWidth), 0, TextureWidth - 1), Math.Clamp((int)(v * TextureHeight), 0, TextureHeight - 1));
        return true;
    }

    public RmlInputEvent PointerEvent(RmlVrRay ray, RmlInputKind kind, int button = 0)
    {
        if (!TryHit(ray, out var hit)) throw new ArgumentException("Ray does not hit the UI plane.", nameof(ray));
        return new(kind, hit.PixelX, hit.PixelY, button);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public static string PreviewMarkup()
        => "<svg class='vs-vr-preview' width='620' height='380' viewBox='0 0 620 380'><path d='M88 82 L508 60 L550 290 L108 316 Z' fill='#192b29' stroke='#62bdac' stroke-width='3'/><path d='M108 316 L70 341 M550 290 L579 320 M88 82 L54 58' stroke='#4a7770' stroke-width='2'/><path d='M145 136 L466 119 L483 249 L159 265 Z' fill='#243c38' stroke='#6ad4bb' stroke-width='2'/><path d='M178 174 H447 M184 212 H456' stroke='#3a7e70' stroke-width='2'/><circle cx='325' cy='190' r='16' fill='none' stroke='#ffbd64' stroke-width='3'/><path d='M325 147 V172 M325 208 V233 M282 190 H307 M343 190 H368' stroke='#ffbd64' stroke-width='2'/><text x='126' y='303' fill='#d1e8df' font-size='16'>1.2 m x 0.7 m</text></svg>";
}
