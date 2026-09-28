using System.Globalization;
using System.Text;

namespace VSRmlUi;

/// <summary>Audio analysis views fed by a host's PCM samples and frequency bins.</summary>
public static class RmlAudioVisualizers
{
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string E(string value) => System.Net.WebUtility.HtmlEncode(value);

    public static string Waveform(string id) => Canvas(id, "vs-waveform", "Waveform");
    public static string FrequencyResponse(string id) => Canvas(id, "vs-frequency-response", "Frequency response");
    public static string Spectrogram(string id) => Canvas(id, "vs-spectrogram", "Spectrogram");

    private static string Canvas(string id, string kind, string label)
        => $"<svg id='{E(id)}' class='vs-audio-chart {kind}' width='720' height='280' viewBox='0 0 720 280' role='img' aria-label='{label}'/>";

    public static void SetWaveform(RmlDocument document, string id, ReadOnlySpan<float> samples, double durationSeconds)
    {
        if (samples.IsEmpty || !double.IsFinite(durationSeconds) || durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(samples));
        var chart = Get(document, id);
        var svg = new StringBuilder(Grid("#164523", 8, 4));
        const int columns = 340;
        for (int x = 0; x < columns; x++)
        {
            int start = x * samples.Length / columns;
            int end = Math.Max(start + 1, (x + 1) * samples.Length / columns);
            float low = 0, high = 0;
            for (int i = start; i < Math.Min(end, samples.Length); i++)
            {
                if (!float.IsFinite(samples[i])) throw new ArgumentException("Samples must be finite.", nameof(samples));
                low = Math.Min(low, samples[i]); high = Math.Max(high, samples[i]);
            }
            double px = 24 + x * 672.0 / (columns - 1);
            svg.Append("<path d='M").Append(N(px)).Append(' ').Append(N(140 - Math.Clamp(high, -1, 1) * 112))
                .Append(" L").Append(N(px)).Append(' ').Append(N(140 - Math.Clamp(low, -1, 1) * 112))
                .Append("' stroke='#45db9e' stroke-width='1'/>");
        }
        svg.Append("<path d='M24 140 H696' stroke='#346b48' stroke-width='1'/>");
        svg.Append("<text x='24' y='274' fill='#a4b8aa' font-size='11'>0 s</text>")
            .Append("<text x='650' y='274' fill='#a4b8aa' font-size='11'>").Append(N(durationSeconds)).Append(" s</text>");
        chart.InnerRml = svg.ToString();
        chart.SetAttribute("aria-label", "Waveform, " + N(durationSeconds) + " seconds");
    }

    public static void SetFrequencyResponse(RmlDocument document, string id, ReadOnlySpan<float> decibels, double maxFrequencyHz)
    {
        if (decibels.Length < 2 || !double.IsFinite(maxFrequencyHz) || maxFrequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(decibels));
        var chart = Get(document, id);
        var svg = new StringBuilder(Grid("#164523", 8, 5));
        var points = new StringBuilder("M");
        for (int i = 0; i < decibels.Length; i++)
        {
            if (!float.IsFinite(decibels[i])) throw new ArgumentException("Decibels must be finite.", nameof(decibels));
            if (i > 0) points.Append(" L");
            points.Append(N(24 + i * 672.0 / (decibels.Length - 1))).Append(' ')
                .Append(N(250 - (Math.Clamp(decibels[i], -100, 0) + 100) * 2.2));
        }
        svg.Append("<path d='").Append(points).Append("' fill='none' stroke='#45db9e' stroke-width='2'/>");
        for (int i = 0; i <= 5; i++)
            svg.Append("<text x='698' y='").Append(N(34 + i * 44)).Append("' fill='#a4b8aa' font-size='10'>").Append(-i * 20).Append("</text>");
        for (int i = 1; i < 8; i++)
            svg.Append("<text x='").Append(N(24 + i * 84)).Append("' y='274' fill='#a4b8aa' font-size='10'>").Append(N(maxFrequencyHz * i / 8000)).Append("k</text>");
        svg.Append("<text x='24' y='274' fill='#a4b8aa' font-size='11'>0 Hz</text>")
            .Append("<text x='625' y='274' fill='#a4b8aa' font-size='11'>").Append(N(maxFrequencyHz / 1000)).Append(" kHz</text>")
            .Append("<text x='4' y='30' fill='#a4b8aa' font-size='11'>dB</text>");
        chart.InnerRml = svg.ToString();
        chart.SetAttribute("aria-label", "Frequency response, 0 to " + N(maxFrequencyHz) + " Hz");
    }

    /// <summary>Values are time-major normalized magnitudes, from low to high frequency within each column.</summary>
    public static void SetSpectrogram(RmlDocument document, string id, ReadOnlySpan<float> magnitudes, int timeBins, int frequencyBins, double maxFrequencyHz)
    {
        if (timeBins is < 2 or > 256 || frequencyBins is < 2 or > 128 || magnitudes.Length != timeBins * frequencyBins || !double.IsFinite(maxFrequencyHz) || maxFrequencyHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(magnitudes));
        var chart = Get(document, id);
        var svg = new StringBuilder();
        for (int x = 0; x < timeBins; x++)
            for (int y = 0; y < frequencyBins; y++)
            {
                float value = magnitudes[x * frequencyBins + y];
                if (!float.IsFinite(value)) throw new ArgumentException("Magnitudes must be finite.", nameof(magnitudes));
                double power = Math.Clamp(value, 0, 1);
                int red = (int)(24 + 231 * Math.Min(1, power * 1.5));
                int green = (int)(7 + 198 * Math.Max(0, (power - 0.35) / 0.65));
                int blue = (int)(39 + 92 * (1 - power));
                svg.Append("<rect x='").Append(N(24 + x * 672.0 / timeBins)).Append("' y='")
                    .Append(N(250 - (y + 1) * 220.0 / frequencyBins)).Append("' width='")
                    .Append(N(672.0 / timeBins + 0.5)).Append("' height='").Append(N(220.0 / frequencyBins + 0.5))
                    .Append("' fill='#").Append(red.ToString("X2")).Append(green.ToString("X2")).Append(blue.ToString("X2")).Append("'/>");
            }
        svg.Append("<text x='24' y='274' fill='#a4b8aa' font-size='11'>Time</text>")
            .Append("<text x='625' y='274' fill='#a4b8aa' font-size='11'>").Append(N(maxFrequencyHz / 1000)).Append(" kHz</text>");
        for (int i = 0; i <= 4; i++)
            svg.Append("<text x='698' y='").Append(N(34 + i * 55)).Append("' fill='#a4b8aa' font-size='10'>").Append(N(maxFrequencyHz * (4 - i) / 4000)).Append("k</text>");
        chart.InnerRml = svg.ToString();
        chart.SetAttribute("aria-label", "Spectrogram, " + timeBins + " time bins and " + frequencyBins + " frequency bins");
    }

    private static RmlElement Get(RmlDocument document, string id)
        => document.GetElementById(id) ?? throw new ArgumentException($"Audio chart '{id}' was not found.", nameof(id));

    private static string Grid(string color, int columns, int rows)
    {
        var svg = new StringBuilder();
        for (int i = 0; i <= columns; i++)
        {
            double x = 24 + 672.0 * i / columns;
            svg.Append("<path d='M").Append(N(x)).Append(" 30 V250' stroke='").Append(color).Append("' stroke-width='1'/>");
        }
        for (int i = 0; i <= rows; i++)
        {
            double y = 30 + 220.0 * i / rows;
            svg.Append("<path d='M24 ").Append(N(y)).Append(" H696' stroke='").Append(color).Append("' stroke-width='1'/>");
        }
        return svg.ToString();
    }
}
