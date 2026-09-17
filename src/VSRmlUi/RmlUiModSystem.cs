// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VSRmlUi;

/// <summary>Obtain from api.ModLoader.GetModSystem&lt;RmlUiModSystem&gt;().Service on the client.</summary>
public sealed class RmlUiModSystem : ModSystem
{
    private ICoreClientAPI? api;
    private RmlRuntime? runtime;
    private bool stopped;
    public string? InitializationError { get; private set; }
    public IRmlUiService? Service => runtime is { IsAvailable: true } ? runtime : null;
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;
    public override double ExecuteOrder() => 0.01;
    public override void StartPre(ICoreAPI api)
    {
        // Fonts are not an ordinary asset category in the base game. Register before asset discovery.
        if (api.Side == EnumAppSide.Client && !AssetCategory.categories.ContainsKey("fonts"))
            _ = new AssetCategory("fonts", false, EnumAppSide.Client);
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        stopped = false;
        this.api = api;
        try
        {
            var host = new GameHost(api);
            runtime = new RmlRuntime(host);
            try { runtime.ColorPalette = api.LoadModConfig<RmlCustomColorPalette>("vsrmlui-colors.json") ?? new(); }
            catch (Exception ex) { api.Logger.Warning("[vsrmlui] Unable to load custom colors: {0}", ex.Message); }
            runtime.ColorPalette.Normalize();
            runtime.SaveColorPalette = () => api.StoreModConfig(runtime.ColorPalette, "vsrmlui-colors.json");
            runtime.Dimensions = () => (Math.Max(1, api.Render.FrameWidth), Math.Max(1, api.Render.FrameHeight), Math.Max(0.25f, RuntimeEnv.GUIScale));
            runtime.CreateView = document => new GameDialog(api, host, document);
            // Montserrat is not present in every Vintage Story distribution (notably
            // dedicated/modpack installs).  A missing font must not abort the whole
            // RmlUi runtime, otherwise Director cannot open its workspace.  Register
            // the bundled Noto font as a reliable default and use game fonts when
            // available.
            TryRegisterFont(runtime, "vsrmlui:fonts/NotoSansCJKsc-Regular.otf", "vsrmlui-default");
            TryRegisterFont(runtime, "game:fonts/Montserrat-Regular.ttf", "vsrmlui-default");
            TryRegisterFont(runtime, "game:fonts/Montserrat-Bold.ttf", "vsrmlui-default", 700);
            TryRegisterFont(runtime, "game:fonts/Montserrat-Italic.ttf", "vsrmlui-default", italic: true);
            TryRegisterFont(runtime, "vsrmlui:fonts/NotoSansCJKsc-Regular.otf", "vsrmlui-cjk", fallback: true);
            api.Logger.Notification("[vsrmlui] RmlUi {0} initialized. API {1}.", runtime.RmlUiVersion, runtime.Version);
            api.Event.LeaveWorld += Stop;
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            api.Logger.Error("[vsrmlui] Initialization failed: {0}", ex);
            runtime?.Dispose(); runtime = null;
        }
    }

    private void TryRegisterFont(RmlRuntime runtime, string path, string family, int weight = 400, bool italic = false, bool fallback = false)
    {
        try { runtime.RegisterFont(path, family, weight, italic, fallback); }
        catch (Exception ex) { api?.Logger.Warning("[vsrmlui] Optional font {0} unavailable: {1}", path, ex.Message); }
    }
    private void Stop()
    {
        if (stopped) return;
        stopped = true;
        runtime?.Dispose(); runtime = null;
    }
    public override void Dispose()
    {
        if (api is not null) api.Event.LeaveWorld -= Stop;
        Stop(); base.Dispose();
    }
}
