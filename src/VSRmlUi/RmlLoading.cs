namespace VSRmlUi;

public enum RmlLoadingStyle { Squares, Ring, Line }

public static class RmlLoading
{
    /// <summary>Link dialog/loading.rcss. Animation is driven by RmlUi's update clock.</summary>
    public static string Markup(string id, RmlLoadingStyle style)
    {
        string content = style switch
        {
            RmlLoadingStyle.Squares => "<span class='load-square q0'/><span class='load-square q1'/><span class='load-square q2'/><span class='load-square q3'/>",
            RmlLoadingStyle.Ring => "<div class='load-ring'/>",
            RmlLoadingStyle.Line => "<div class='load-line'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
        return $"<div id='{System.Net.WebUtility.HtmlEncode(id)}' class='vs-loading {style.ToString().ToLowerInvariant()}' role='progressbar' aria-label='Loading'>{content}</div>";
    }
}
