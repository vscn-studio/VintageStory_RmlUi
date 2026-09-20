// Copyright (c) 2026 VSCN-Studio
// SPDX-License-Identifier: MIT

using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;
using VSRmlUi;

internal static class FontAssetChecks
{
    internal static void Run(string root, string gameRoot, Action<bool, string> check)
    {
        AssetCategory.categories.Remove("fonts", out var previousCategory);
        try
        {
            // Reproduce the real cold-start order using the game's asset manager and origins.
            var assets = new AssetManager(Path.Combine(gameRoot, "assets"), EnumAppSide.Client);
            assets.InitAndLoadBaseAssets(null);
            var messages = new List<string>();
            var logger = Proxy<ILogger>((method, args) =>
            {
                if (method.Name is "Error" or "Warning") messages.Add(args?[0]?.ToString() ?? "");
                return null;
            });
            var api = Proxy<ICoreClientAPI>((method, _) => method.Name switch
            {
                "get_Side" => EnumAppSide.Client,
                "get_Assets" => assets,
                "get_Logger" => logger,
                _ => method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null
            });
            var mod = new RmlUiModSystem();
            mod.StartPre(api);
            assets.CustomModOrigins.Add(new FolderOrigin(Path.Combine(root, "src", "VSRmlUi")));
            assets.AddExternalAssets(logger);
            var regular = new AssetLocation("game:fonts/Montserrat-Regular.ttf");
            check(File.Exists(Path.Combine(gameRoot, "assets/game/fonts/Montserrat-Regular.ttf"))
                && assets.TryGet(regular) is null,
                "cold startup reproduces installed game font missing from the base asset index");

            mod.AssetsLoaded(api);
            var host = new GameHost(api);
            foreach (string file in Directory.EnumerateFiles(Path.Combine(gameRoot, "assets/game/fonts"), "*.ttf"))
            {
                string path = "game:fonts/" + Path.GetFileName(file);
                byte[] expected = File.ReadAllBytes(file);
                check(host.ReadAsset(path).SequenceEqual(expected)
                    && host.ReadAsset(path.ToLowerInvariant()).SequenceEqual(expected),
                    "game asset host loads real font with either asset-path casing: " + Path.GetFileName(file));
            }
            check(host.ReadAsset("vsrmlui:fonts/NotoSansCJKsc-Regular.otf").Length > 0,
                "font rescan preserves bundled CJK assets");
            using (var runtime = new RmlRuntime(host, headless: true))
            {
                runtime.RegisterFont("game:fonts/Montserrat-Regular.ttf", "vsrmlui-default");
                runtime.RegisterFont("game:fonts/Montserrat-Bold.ttf", "vsrmlui-default", 700);
                runtime.RegisterFont("game:fonts/Montserrat-Italic.ttf", "vsrmlui-default", italic: true);
                runtime.RegisterFont("vsrmlui:fonts/NotoSansCJKsc-Regular.otf", "vsrmlui-cjk", fallback: true);
                using var document = runtime.LoadDocumentFromString("fontchecks",
                    "<rml><head><style>body { font-family: vsrmlui-default; font-size: 18px; }</style></head>"
                    + "<body>Regular 中文 <span style='font-weight: bold;'>Bold</span> <span style='font-style: italic;'>Italic</span></body></rml>",
                    "fontchecks:dialog/fonts.rml");
                document.Show();
                document.Call(3, 800, 600, 1);
                check(messages.Count == 0, "real native font loading and layout produce no resource or font warnings");
            }

            byte[] overridden = File.ReadAllBytes(Path.Combine(gameRoot, "assets/game/fonts/Lora-Regular.ttf"));
            assets.Add(regular, new Asset(overridden, regular, assets.Origins[0]));
            mod.AssetsLoaded(api);
            check(host.ReadAsset("game:fonts/Montserrat-Regular.ttf").SequenceEqual(overridden),
                "font rescan retains runtime asset overrides");
            mod.AssetsLoaded(Proxy<ICoreAPI>((method, _) => method.Name == "get_Side"
                ? EnumAppSide.Server : throw new InvalidOperationException("Server accessed client fonts")));
            check(true, "server asset phase does not access client fonts");
        }
        finally
        {
            AssetCategory.categories.Remove("fonts");
            if (previousCategory is not null) AssetCategory.categories["fonts"] = previousCategory;
        }
    }

    private static T Proxy<T>(System.Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, GameIntegrationChecks.ApiProxy>();
        ((GameIntegrationChecks.ApiProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
