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

    internal static void NodeEditor(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        var graph = new RmlNodeEditor("graph");
        graph.AddNode(new("source", "Audio input", 70, 95, [], [new("signal", "Signal", "#41d9ac")]));
        graph.AddNode(new("filter", "Low-pass filter", 370, 165, [new("input", "Audio", "#41d9ac")], [new("output", "Filtered", "#ffae57")]));
        graph.AddNode(new("output", "Output", 690, 120, [new("input", "Signal", "#ffae57")], []));
        graph.Connect(new("source", "signal", "filter", "input"));
        graph.Connect(new("filter", "output", "output", "input"));
        using var page = Page(ui, "node-editor", "Node editor", graph.Markup());
        graph.Bind(page);
        preview(page, "node-editor-default");
        string before = page.GetElementById("graph-wires")!.InnerRml;
        graph.MoveNode("filter", 400, 270);
        preview(page, "node-editor-moved");
        check(before != page.GetElementById("graph-wires")!.InnerRml && graph.Links.Count == 2, "node editor recalculates colored links after a node moves");
        graph.Unbind();
    }

    internal static void Knob(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        using var page = Page(ui, "knob", "Volume knob", RmlKnob.Markup("volume"));
        RmlKnob.SetValue(page, "volume", 0.25);
        preview(page, "knob-low");
        string before = page.GetElementById("volume-ring")!.InnerRml;
        RmlKnob.SetValue(page, "volume", 0.82);
        preview(page, "knob-high");
        check(before != page.GetElementById("volume-ring")!.InnerRml && page.GetElementById("volume")!.GetAttribute("aria-valuenow") == "82", "knob arc and value respond to volume");
    }
}
