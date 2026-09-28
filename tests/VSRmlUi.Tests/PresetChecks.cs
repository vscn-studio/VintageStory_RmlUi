using VSRmlUi;

static class PresetChecks
{
    internal static RmlDocument Page(RmlRuntime ui, string stylesheet, string title, string markup)
    {
        var page = ui.LoadDocumentFromString("vsrmlui", $"<rml><head><link type='text/rcss' href='vsrmlui:dialog/theme.rcss'/><link type='text/rcss' href='vsrmlui:dialog/{stylesheet}.rcss'/><style>body {{ width: 100%; height: 100%; background-color: #202020; }} .demo {{ padding: 36dp; }} .demo-row {{ display: flex; align-items: center; margin: 24dp 0dp; }} .demo-row > * {{ margin-right: 20dp; }} </style></head><body><div class='demo'><h2>{title}</h2>{markup}</div></body></rml>", $"vsrmlui:dialog/{stylesheet}-preview.rml");
        page.Show();
        return page;
    }

    internal static void Loading(RmlRuntime ui, Action<RmlDocument, string> preview, Func<byte[]> pixels, Action<bool, string> check)
    {
        using var page = Page(ui, "loading", "Loading", "<div class='demo-row'>" + RmlLoading.Markup("squares", RmlLoadingStyle.Squares) + RmlLoading.Markup("ring", RmlLoadingStyle.Ring) + RmlLoading.Markup("line", RmlLoadingStyle.Line) + "</div>");
        preview(page, "loading-frame-1");
        var first = pixels();
        Thread.Sleep(340);
        preview(page, "loading-frame-2");
        check(!first.SequenceEqual(pixels()), "loading presets animate across rendered frames");
    }
}
