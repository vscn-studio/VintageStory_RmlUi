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
        // Register before mod asset discovery. The game's base assets are already indexed by this phase.
        if (api.Side == EnumAppSide.Client && !AssetCategory.categories.ContainsKey("fonts"))
            _ = new AssetCategory("fonts", false, EnumAppSide.Client);
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        // Include base-game fonts that were skipped before StartPre registered the category.
        // Let the asset manager resolve physical filename casing and mod/theme overrides.
        if (api.Side == EnumAppSide.Client)
            api.Assets.Reload(AssetCategory.categories["fonts"]);
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
            SystemFontResolver.Register(runtime, api);
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
