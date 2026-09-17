// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VSRmlUi;

namespace VSRmlUiExample;

public sealed class ExampleModSystem : ModSystem
{
    private IRmlUiService? ui;
    private RmlDocument? window, hud, modal, inputTest;
    private readonly List<IDisposable> inputTestSubscriptions = [];
    private readonly List<string> inputTestEvents = [];
    private int clicks;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        ui = api.ModLoader.GetModSystem<RmlUiModSystem>().Service;
        if (ui is null) { api.Logger.Error("[vsrmluiexample] RmlUi is unavailable; inspect the vsrmlui initialization log."); return; }
        api.Input.RegisterHotKey("vsrmlui-example", Lang.Get("vsrmluiexample:hotkey"), GlKeys.F8, HotkeyType.GUIOrOtherControls, false, true);
        api.Input.SetHotKeyHandler("vsrmlui-example", _ => { Toggle(); return true; });
        api.Input.RegisterHotKey("vsrmlui-input-test", Lang.Get("vsrmluiexample:input-test-hotkey"), GlKeys.F9, HotkeyType.GUIOrOtherControls, false, true);
        api.Input.SetHotKeyHandler("vsrmlui-input-test", _ => { ToggleInputTest(); return true; });
    }
    private void Toggle()
    {
        if (ui is not { IsAvailable: true }) return;
        if (window is null || window.IsDisposed) Create();
        if (window!.IsVisible) window.Close(); else window.Show();
    }
    private void Create()
    {
        window = ui!.LoadDocument("vsrmluiexample", "vsrmluiexample:dialog/example.rml");
        hud = ui.LoadDocument("vsrmluiexample", "vsrmluiexample:dialog/hud.rml", new() { Mode = RmlWindowMode.Hud, DrawOrder = 0.08 });
        window.GetElementById("close")!.On("click", _ => window.Close());
        window.GetElementById("count")!.On("click", _ =>
        {
            clicks++;
            window.GetElementById("status")!.Text = Lang.Get("vsrmluiexample:clicks", clicks);
            hud.GetElementById("hud-count")!.Text = clicks.ToString();
        });
        window.GetElementById("name")!.On("change", e => window.GetElementById("status")!.Text = Lang.Get("vsrmluiexample:hello", e.Value));
        window.GetElementById("volume")!.On("change", e => window.GetElementById("volume-value")!.Text = e.Value);
        window.GetElementById("theme")!.On("change", e => window.Root.SetClass("stone", e.Value == "stone"));
        window.GetElementById("show-hud")!.On("change", _ => { if (hud.IsVisible) hud.Close(); else hud.Show(); });
        window.GetElementById("modal")!.On("click", _ => ShowModal());
        window.GetElementById("add")!.On("click", _ =>
        {
            var row = window.GetElementById("items")!.AppendChild("p");
            row.Text = Lang.Get("vsrmluiexample:dynamic-row");
            row.On("click", _ => row.Remove());
        });
    }
    private void ShowModal()
    {
        modal?.Dispose();
        modal = ui!.LoadDocumentFromString("vsrmluiexample", """
            <rml><head><link type="text/rcss" href="vsrmlui:dialog/theme.rcss" />
            <style>body { width: 100%; height: 100%; background-color: #0008; }
            #box { position: absolute; left: 20%; top: 25%; width: 60%; }</style></head>
            <body><div id="box" class="vs-window"><h1>[[vsrmluiexample:modal-title]]</h1>
            <p>[[vsrmluiexample:modal-description]]</p><button id="ok">[[vsrmluiexample:ok]]</button></div></body></rml>
            """, "vsrmluiexample:dialog/modal.rml", new() { Mode = RmlWindowMode.Modal, DrawOrder = 0.3 });
        modal.GetElementById("ok")!.On("click", _ => modal.Close());
        modal.Show();
    }
    private void ToggleInputTest()
    {
        if (ui is not { IsAvailable: true }) return;
        if (inputTest is null || inputTest.IsDisposed) CreateInputTest();
        if (inputTest!.IsVisible) inputTest.Close(); else inputTest.Show();
    }
    private void CreateInputTest()
    {
        inputTest = ui!.LoadDocument("vsrmluiexample", "vsrmluiexample:dialog/input-test.rml", new() { DrawOrder = 0.35 });
        inputTest.GetElementById("close")!.On("click", _ => inputTest.Close());
        inputTest.GetElementById("clear-log")!.On("click", _ => ClearInputLog());
        inputTest.GetElementById("reset")!.On("click", _ => ResetInputTest());
        foreach (string id in new[] { "single", "multiline", "number", "choice", "check", "range" })
        {
            var element = inputTest.GetElementById(id)!;
            foreach (string type in new[] { "focus", "blur", "input", "change", "keydown", "keyup" })
                inputTestSubscriptions.Add(element.On(type, e => RecordInputEvent(e)));
        }
        foreach (string id in new[] { "reset", "clear-log", "close" })
            inputTestSubscriptions.Add(inputTest.GetElementById(id)!.On("click", e => RecordInputEvent(e)));
        ClearInputLog();
    }
    private void RecordInputEvent(RmlEvent e)
    {
        if (inputTest is null || inputTest.IsDisposed) return;
        string value = e.Value.Length > 40 ? e.Value[..40] + "…" : e.Value;
        string line = $"{e.Type,-7} target={e.TargetId,-9} value={value} modifiers={e.Modifiers}";
        inputTestEvents.Add(line);
        if (inputTestEvents.Count > 12) inputTestEvents.RemoveAt(0);
        inputTest.GetElementById("event-log")!.Text = string.Join("\n", inputTestEvents);
        inputTest.GetElementById("status")!.Text = $"Last event: {line}";
    }
    private void ClearInputLog()
    {
        inputTestEvents.Clear();
        if (inputTest is null || inputTest.IsDisposed) return;
        inputTest.GetElementById("event-log")!.Text = "(events will appear here)";
        inputTest.GetElementById("status")!.Text = "Ready. Type text, use an IME, and try the editing shortcuts.";
    }
    private void ResetInputTest()
    {
        if (inputTest is null || inputTest.IsDisposed) return;
        inputTest.GetElementById("single")!.Value = "中文 / English";
        inputTest.GetElementById("multiline")!.Value = "Paste or compose text here…";
        inputTest.GetElementById("number")!.Value = "42";
        inputTest.GetElementById("choice")!.Value = "ime";
        inputTest.GetElementById("range")!.Value = "50";
        ClearInputLog();
    }
    public override void Dispose()
    {
        foreach (var subscription in inputTestSubscriptions) subscription.Dispose();
        inputTestSubscriptions.Clear(); inputTestEvents.Clear(); inputTest = null;
        if (ui is { IsAvailable: true }) ui.ReleaseAll("vsrmluiexample");
        ui = null;
    }
}
