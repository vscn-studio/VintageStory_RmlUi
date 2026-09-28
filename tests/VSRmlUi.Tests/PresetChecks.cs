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

    internal static void Switch(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        using var page = Page(ui, "switch", "Switch", RmlSwitch.Markup("enabled"));
        preview(page, "switch-off");
        var left = page.QuerySelector(".vs-switch-thumb")!.Bounds.X;
        bool changed = false;
        using var binding = RmlSwitch.Bind(page, "enabled", value => changed = value);
        page.GetElementById("enabled")!.DispatchEvent("click");
        Thread.Sleep(220);
        preview(page, "switch-on");
        check(changed && page.GetElementById("enabled")!.GetAttribute("aria-checked") == "true" && page.QuerySelector(".vs-switch-thumb")!.Bounds.X > left, "switch click updates state, callback and thumb position");
    }

    internal static void MidiOsc(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        var midi = MidiMessage.NoteOn(2, 64, 100);
        check(midi.Channel == 2 && midi.Command == 0x90, "MIDI message preserves channel and command");
        var decoded = OscCodec.Decode(OscCodec.Encode(new("/synth/cutoff", [440, 0.75f, "Hz"])));
        check(decoded.Address == "/synth/cutoff" && (int)decoded.Arguments[0] == 440 && (float)decoded.Arguments[1] == 0.75f, "OSC codec round trips typed arguments");
        using var receiver = new OscUdpPort();
        using var sender = new OscUdpPort();
        sender.SendAsync(new("/synth/cutoff", [440]), new(System.Net.IPAddress.Loopback, receiver.LocalPort)).GetAwaiter().GetResult();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        check(receiver.ReceiveAsync(timeout.Token).GetAwaiter().GetResult().Address == "/synth/cutoff", "OSC UDP loopback receives a message");
        using var page = Page(ui, "midi-osc", "MIDI / OSC", RmlMidiOscMonitor.Markup("monitor"));
        RmlMidiOscMonitor.Show(page, "monitor", "MIDI ch 3", "Note on  E4  velocity 100");
        preview(page, "midi-osc-midi");
        RmlMidiOscMonitor.Show(page, "monitor", "OSC", "/synth/cutoff  440 Hz");
        preview(page, "midi-osc-osc");
    }

    internal static void VrPlane(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        var plane = new RmlVrPlane();
        check(plane.TryHit(new(new(0, 0, 1), -System.Numerics.Vector3.UnitZ), out var center) && center.PixelX == 600 && center.PixelY == 350, "VR center ray maps to UI center pixel");
        check(!plane.TryHit(new(new(2, 0, 1), -System.Numerics.Vector3.UnitZ), out _), "VR ray outside plane is rejected");
        plane.Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, MathF.PI / 2);
        check(plane.TryHit(new(new(1, 0, 0), -System.Numerics.Vector3.UnitX), out var rotated) && rotated.PixelX == 600, "VR rotated plane maps controller ray");
        using var page = Page(ui, "vr-plane", "VR UI plane", RmlVrPlane.PreviewMarkup());
        preview(page, "vr-plane");
    }

    internal static void DatePicker(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        var picker = new RmlDatePicker("calendar", new DateOnly(2026, 9, 28));
        using var page = Page(ui, "date-picker", "Date picker", picker.Markup());
        picker.Bind(page);
        preview(page, "date-picker-september");
        picker.MoveMonth(1);
        preview(page, "date-picker-october");
        page.Root.QuerySelector(".vs-date-day[data-date='2026-10-15']")!.DispatchEvent("click");
        check(picker.Selected == new DateOnly(2026, 10, 15) && page.GetElementById("calendar-grid")!.InnerRml.Contains("2026-10-15"), "date picker changes month and selected day");
        picker.Unbind();
    }

    internal static void Orientation(RmlRuntime ui, Action<RmlDocument, string> preview, Action<bool, string> check)
    {
        var widget = new RmlOrientationWidget("axes");
        using var page = Page(ui, "orientation", "3D orientation", widget.Markup());
        widget.Bind(page);
        preview(page, "orientation-front");
        string before = page.GetElementById("axes-axes")!.InnerRml;
        widget.Set(page, 1.1f, -0.45f);
        preview(page, "orientation-rotated");
        check(before != page.GetElementById("axes-axes")!.InnerRml && page.GetElementById("axes")!.GetAttribute("aria-label").Contains("yaw 1.1"), "3D orientation projects axes after rotation");
        widget.Unbind();
    }
}
