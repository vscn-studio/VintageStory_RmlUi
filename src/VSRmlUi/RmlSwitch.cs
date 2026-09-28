namespace VSRmlUi;

public static class RmlSwitch
{
    public static string Markup(string id, bool enabled = false)
        => $"<button id='{System.Net.WebUtility.HtmlEncode(id)}' class='vs-switch{(enabled ? " on" : "")}' role='switch' aria-checked='{enabled.ToString().ToLowerInvariant()}' type='button'><span class='vs-switch-thumb'/></button>";

    public static void Set(RmlDocument document, string id, bool enabled)
    {
        var toggle = document.GetElementById(id) ?? throw new ArgumentException("Switch not found.", nameof(id));
        toggle.SetClass("on", enabled);
        toggle.SetAttribute("aria-checked", enabled ? "true" : "false");
        toggle.QuerySelector(".vs-switch-thumb")!.SetProperty("left", enabled ? "25dp" : "3dp");
    }

    public static IDisposable Bind(RmlDocument document, string id, Action<bool> changed)
    {
        var toggle = document.GetElementById(id) ?? throw new ArgumentException("Switch not found.", nameof(id));
        return toggle.On("click", _ => { bool next = toggle.GetAttribute("aria-checked") != "true"; Set(document, id, next); changed(next); });
    }
}
