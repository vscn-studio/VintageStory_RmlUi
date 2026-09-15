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
    private RmlDocument? window, hud, modal;
    private int clicks;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        ui = api.ModLoader.GetModSystem<RmlUiModSystem>().Service;
        if (ui is null) { api.Logger.Error("[vsrmluiexample] RmlUi is unavailable; inspect the vsrmlui initialization log."); return; }
        api.Input.RegisterHotKey("vsrmlui-example", Lang.Get("vsrmluiexample:hotkey"), GlKeys.F8, HotkeyType.GUIOrOtherControls, false, true);
        api.Input.SetHotKeyHandler("vsrmlui-example", _ => { Toggle(); return true; });
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
    public override void Dispose()
    {
        if (ui is { IsAvailable: true }) ui.ReleaseAll("vsrmluiexample");
        ui = null;
    }
}
