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
    private RmlDocument? window, hud, modal, inputTest, toolWindow;
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
        void ShowView(bool items)
        {
            window.GetElementById("panel")!.SetClass("items-view", items);
            window.GetElementById("nav-editor")!.SetClass("active", !items);
            window.GetElementById("nav-items")!.SetClass("active", items);
        }
        window.GetElementById("nav-editor")!.On("click", _ => ShowView(false));
        window.GetElementById("nav-items")!.On("click", _ => ShowView(true));
        window.GetElementById("count")!.On("click", _ =>
        {
            clicks++;
            window.GetElementById("status")!.Text = Lang.Get("vsrmluiexample:clicks", clicks);
            hud.GetElementById("hud-count")!.Text = clicks.ToString();
        });
        window.GetElementById("name")!.On("change", e => window.GetElementById("status")!.Text = Lang.Get("vsrmluiexample:hello", e.Value));
        window.GetElementById("volume")!.On("change", e => window.GetElementById("volume-value")!.Text = e.Value);
        window.GetElementById("theme")!.On("change", e =>
        {
            var panel = window.GetElementById("panel")!;
            foreach (string variant in new[] { "night", "day", "contrast" })
                panel.SetClass("vs-theme-" + variant, e.Value == variant);
        });
        window.GetElementById("show-hud")!.On("change", _ => { if (hud.IsVisible) hud.Close(); else hud.Show(); });
        window.GetElementById("modal")!.On("click", _ => ShowModal());
        window.GetElementById("open-tool")!.On("click", _ => ShowToolWindow());
        window.GetElementById("add")!.On("click", _ =>
        {
            var row = window.GetElementById("items")!.AppendChild("p");
            row.SetClass("vs-list-item", true);
            row.Text = Lang.Get("vsrmluiexample:dynamic-row");
            row.On("click", _ => row.Remove());
        });
    }
    private void ShowToolWindow()
    {
        if (toolWindow is null || toolWindow.IsDisposed)
        {
            toolWindow = ui!.LoadDocument("vsrmluiexample", "vsrmluiexample:dialog/tabbed-tool.rml", new() { DrawOrder = 0.25 });
            void SelectTab(string page)
            {
                var panel = toolWindow.GetElementById("tool-panel")!;
                panel.SetClass("output-page", page == "output");
                panel.SetClass("advanced-page", page == "advanced");
                foreach (string name in new[] { "general", "output", "advanced" })
                    toolWindow.GetElementById("tab-" + name)!.SetClass("active", name == page);
            }
            foreach (string name in new[] { "general", "output", "advanced" })
            {
                string page = name;
                toolWindow.GetElementById("tab-" + name)!.On("click", _ => SelectTab(page));
            }
            toolWindow.GetElementById("tool-close")!.On("click", _ => toolWindow.Close());
            toolWindow.GetElementById("tool-cancel")!.On("click", _ => toolWindow.Close());
            toolWindow.GetElementById("tool-apply")!.On("click", _ =>
            {
                if (window is { IsDisposed: false })
                    window.GetElementById("status")!.Text = Lang.Get("vsrmluiexample:applied", toolWindow.GetElementById("tool-name")!.Value);
                toolWindow.Close();
            });
            foreach (string name in new[] { "scale", "quality" })
                toolWindow.GetElementById("tool-" + name)!.On("change", e => toolWindow.GetElementById("tool-" + name + "-value")!.Text = e.Value);
            toolWindow.GetElementById("tool-theme")!.On("change", e =>
            {
                var panel = toolWindow.GetElementById("tool-panel")!;
                foreach (string variant in new[] { "night", "day", "contrast" })
                    panel.SetClass("vs-theme-" + variant, e.Value == variant);
            });
        }
        toolWindow.Show();
    }
    private void ShowModal()
    {
        modal?.Dispose();
        modal = ui!.LoadDocument("vsrmluiexample", "vsrmluiexample:dialog/modal.rml", new() { Mode = RmlWindowMode.Modal, DrawOrder = 0.3 });
        var source = window!.GetElementById("panel")!.ClassNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string variant in new[] { "night", "day", "contrast" })
            modal.GetElementById("box")!.SetClass("vs-theme-" + variant, source.Contains("vs-theme-" + variant));
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
        inputTestSubscriptions.Clear(); inputTestEvents.Clear(); inputTest = null; toolWindow = null;
        if (ui is { IsAvailable: true }) ui.ReleaseAll("vsrmluiexample");
        ui = null;
    }
}
