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

    internal static void ClickFeedback(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        using var page = Page(ui, "theme", "Click feedback", "<div class='demo-row'><button id='action' class='vs-primary'>Apply</button><input id='toggle' class='checkbox' type='checkbox'/></div>");
        preview(page, "feedback-rest");
        var button = page.GetElementById("action")!.Bounds;
        page.Call(5, (int)(button.X + button.Width / 2), (int)(button.Y + button.Height / 2));
        page.Call(6, (int)(button.X + button.Width / 2), (int)(button.Y + button.Height / 2));
        preview(page, "feedback-pressed");
        page.Call(7, (int)(button.X + button.Width / 2), (int)(button.Y + button.Height / 2));
        var checkbox = page.GetElementById("toggle")!;
        checkbox.SetAttribute("checked", "checked");
        preview(page, "feedback-checked");
        check(page.QuerySelector("input:checked") is not null && checkbox.Bounds.Width == 24, "checkbox feedback keeps selected state and stable layout");
    }
}
