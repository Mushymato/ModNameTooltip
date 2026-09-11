global using SObject = StardewValley.Object;
using System.Diagnostics;
using HarmonyLib;
using ModNameTooltip.Features;
using ModNameTooltip.Integration;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Characters;
using StardewValley.GameData.Objects;

namespace ModNameTooltip;

public sealed class ModEntry : Mod
{
#if DEBUG
    private const LogLevel DEFAULT_LOG_LEVEL = LogLevel.Debug;
#else
    private const LogLevel DEFAULT_LOG_LEVEL = LogLevel.Trace;
#endif

    public const string ModId = "mushymato.ModNameTooltip";
    private static IMonitor mon = null!;
    internal static IModHelper help = null!;
    internal static ModConfig config = null!;

    public const string Asset_ModNames = $"{ModId}/ModNames";

    internal static readonly Harmony harmony = new(ModId);

    internal static readonly Dictionary<IAssetName, TraceContext> traceCtx = [];
    internal static readonly Dictionary<string, TraceContext> itemTypeToTraceCtx = [];
    internal static TraceContext craftingRecipeCtx = null!;
    internal static TraceContext cookingRecipeCtx = null!;
    internal static TraceContext npcTraceCtx = null!;
    internal static TraceContext farmAnimalTraceCtx = null!;
    internal static TraceContext petTraceCtx = null!;
    internal static TraceContext cropTraceCtx = null!;
    internal static TraceContext wildTreeTraceCtx = null!;
    internal static TraceContext fruitTreeTraceCtx = null!;
    internal static TraceContext buildingsTraceCtx = null!;
    internal static TraceContext locationsTraceCtx = null!;

    internal static readonly ModNameAPI modNameAPI = new();
    internal static readonly PerScreen<Draw_CursorHUD> drawCursorHUD = new(() => new(Context.ScreenId));
    internal static int dataLocationReadyTick = -1;

    public override void Entry(IModHelper helper)
    {
        I18n.Init(helper.Translation);
        mon = Monitor;
        help = helper;
        config = helper.ReadConfig<ModConfig>();

        if (!TryPatchEdit())
        {
            return;
        }

        help.Events.Content.AssetReady += OnAssetReady;
        help.Events.Content.AssetRequested += OnAssetRequested;
        help.Events.GameLoop.GameLaunched += OnGameLaunched;
        help.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        help.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        help.Events.Player.Warped += OnWarped;
        help.Events.Input.CursorMoved += OnCursorMoved;
        help.Events.Input.ButtonsChanged += OnButtonsChanged;
        help.Events.Display.RenderedHud += OnRenderedHud;

        AddItemTraceCtx("(O)", "Data/Objects");
        AddItemTraceCtx("(BC)", "Data/BigCraftables");
        AddItemTraceCtx("(F)", "Data/Furniture");
        AddItemTraceCtx("(W)", "Data/Weapons");
        AddItemTraceCtx("(B)", "Data/Boots");
        AddItemTraceCtx("(H)", "Data/hats");
        AddItemTraceCtx("(M)", "Data/Mannequins");
        AddItemTraceCtx("(P)", "Data/Pants");
        AddItemTraceCtx("(S)", "Data/Shirts");
        AddItemTraceCtx("(T)", "Data/Tools");
        AddItemTraceCtx("(TR)", "Data/Trinkets");
        // special handling for walls and floors
        AddTraceCtxForWallsAndFloors();

        craftingRecipeCtx = AddTraceCtx("Data/CraftingRecipes");
        cookingRecipeCtx = AddTraceCtx("Data/CookingRecipes");
        npcTraceCtx = AddTraceCtx("Data/Characters");
        farmAnimalTraceCtx = AddTraceCtx("Data/FarmAnimals");
        petTraceCtx = AddTraceCtx("Data/Pets");
        cropTraceCtx = AddTraceCtx("Data/Crops");
        wildTreeTraceCtx = AddTraceCtx("Data/WildTrees");
        fruitTreeTraceCtx = AddTraceCtx("Data/FruitTrees");
        buildingsTraceCtx = AddTraceCtx("Data/Buildings");
        locationsTraceCtx = AddTraceCtx("Data/Locations");

        AddLocationEventTraceCtx("Farm");

        help.ConsoleCommands.Add("mnt-print", "Print currently traced mod names", ConsoleDebugPrint);
        help.ConsoleCommands.Add("mnt-perf", "Test performance on tracing of events", ConsoleTestPerf);

        Patch_HoverText.Toggle();
        Patch_BetterCrafting.Apply();
    }

    private static bool TryPatchEdit()
    {
        try
        {
            harmony.Patch(
                original: AccessTools.DeclaredMethod(
                    typeof(AssetRequestedEventArgs),
                    nameof(AssetRequestedEventArgs.Edit)
                ),
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(AssetRequestedEventArgs_Edit_Prefix))
                {
                    priority = Priority.First,
                }
            );
        }
        catch (Exception ex)
        {
            Log($"Failed to patch AssetRequestedEventArgs.Edit\n{ex}", LogLevel.Error);
            return false;
        }

        return true;
    }

    private void ConsoleDebugPrint(string arg1, string[] arg2)
    {
        foreach (TraceContext ctx in traceCtx.Values)
        {
            Log(ctx.TracedAsset.Name, LogLevel.Info);
            foreach ((string key, ModNameInfo modNameText) in ctx.KeyToMod)
            {
                Log($"- {key} : {modNameText.ModName} ({modNameText.ModId})", LogLevel.Info);
            }
        }
    }

    private void ConsoleTestPerf(string arg1, string[] arg2)
    {
        const int trials = 50;
        IAssetName assetName = Helper.GameContent.ParseAssetName("Data/Objects");
        TraceContext ctx = traceCtx[assetName];
        // warmup
        {
            help.GameContent.InvalidateCache(assetName);
            var _ = help.GameContent.Load<Dictionary<string, ObjectData>>(assetName);
        }
        // baseline
        ctx.active = false;
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < trials; i++)
        {
            help.GameContent.InvalidateCache(assetName);
            var _ = help.GameContent.Load<Dictionary<string, ObjectData>>(assetName);
        }
        stopwatch.Stop();
        Log($"{assetName} A: {stopwatch.Elapsed.TotalMilliseconds}", LogLevel.Debug);
        double baselineValue = stopwatch.Elapsed.TotalMilliseconds;

        ctx.active = true;
        stopwatch.Restart();
        for (int i = 0; i < trials; i++)
        {
            help.GameContent.InvalidateCache(assetName);
            var _ = help.GameContent.Load<Dictionary<string, ObjectData>>(assetName);
        }
        stopwatch.Stop();
        Log($"{assetName} B: {stopwatch.Elapsed.TotalMilliseconds}", LogLevel.Debug);
        Log($"{assetName} Compare {stopwatch.Elapsed.TotalMilliseconds / baselineValue:P2}", LogLevel.Info);
    }

    private void ConsoleTestPerfEvents(string arg1, string[] arg2)
    {
        const int trials = 10;
        int count = 0;
        double compares = 0;
        foreach ((IAssetName assetName, TraceContext ctx) in traceCtx)
        {
            if (!ctx.isEvent)
                continue;
            if (!help.GameContent.DoesAssetExist<Dictionary<string, string>>(assetName))
                continue;
            // warmup
            {
                help.GameContent.InvalidateCache(assetName);
                var _ = help.GameContent.Load<Dictionary<string, string>>(assetName);
            }
            // baseline
            ctx.active = false;
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < trials; i++)
            {
                help.GameContent.InvalidateCache(assetName);
                var _ = help.GameContent.Load<Dictionary<string, string>>(assetName);
            }
            stopwatch.Stop();
            Log($"{assetName} A: {stopwatch.Elapsed.TotalMilliseconds}", LogLevel.Debug);
            double baselineValue = stopwatch.Elapsed.TotalMilliseconds;

            ctx.active = true;
            stopwatch.Restart();
            for (int i = 0; i < trials; i++)
            {
                help.GameContent.InvalidateCache(assetName);
                var _ = help.GameContent.Load<Dictionary<string, string>>(assetName);
            }
            stopwatch.Stop();
            Log($"{assetName} B: {stopwatch.Elapsed.TotalMilliseconds}", LogLevel.Debug);
            Log($"{assetName} Compare {stopwatch.Elapsed.TotalMilliseconds / baselineValue:P2}", LogLevel.Info);
            compares += stopwatch.Elapsed.TotalMilliseconds / baselineValue;
            count++;
        }
        Log($"Aggregate {compares / count:P2}", LogLevel.Info);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        drawCursorHUD.Value.OnNewLocation(Game1.currentLocation);
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        drawCursorHUD.Value.OnNewLocation(e.NewLocation);
    }

    private void OnCursorMoved(object? sender, CursorMovedEventArgs e)
    {
        drawCursorHUD.Value.CheckTile(e.NewPosition.Tile);
    }

    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (config.Toggle_HUD.JustPressed())
        {
            config.SetEnable_HUD(!config.Enable_HUD);
        }
        if (config.HoldToShow_HUD.IsBound)
        {
            config.SetHoldingToShow(config.HoldToShow_HUD.IsDown());
        }
        if (config.Toggle_Tooltip.JustPressed())
        {
            config.SetEnable_Tooltip(!config.Enable_Tooltip);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        ModNameInfo.MaybeUpdateMenuColor();
        if (Game1.ticks == dataLocationReadyTick + 1)
        {
            foreach (string locationId in Game1.locationData.Keys)
            {
                if (!locationId.StartsWith("Farm_"))
                    AddLocationEventTraceCtx(locationId);
            }
        }
        drawCursorHUD.Value.OnUpdateTicked(e);
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        drawCursorHUD.Value.OnRenderedHud(e);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        config.Register(
            ModManifest,
            Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu")
        );
    }

    public override object? GetApi()
    {
        return modNameAPI;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(Asset_ModNames))
        {
            string stringsAsset = Path.Combine("i18n", e.Name.LanguageCode.ToString() ?? "default", "mod-names.json");
            if (File.Exists(Path.Combine(help.DirectoryPath, stringsAsset)))
                e.LoadFromModFile<Dictionary<string, string>>(stringsAsset, AssetLoadPriority.Exclusive);
            else
                e.LoadFromModFile<Dictionary<string, string>>(
                    "i18n/default/mod-names.json",
                    AssetLoadPriority.Exclusive
                );
            return;
        }
        foreach (TraceContext ctx in traceCtx.Values)
        {
            // do a bogus edit so that our prefix always happens bolb
            if (ctx.active && ctx.TracedAsset.IsEquivalentTo(e.NameWithoutLocale))
                e.Edit(BogusEdit, AssetEditPriority.Early);
        }
    }

    private static void BogusEdit(IAssetData data) { }

    private void OnAssetReady(object? sender, AssetReadyEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("LooseSprites/Cursors"))
        {
            ModNameInfo.ResetMenuColor();
            return;
        }
        if (traceCtx.TryGetValue(e.NameWithoutLocale, out TraceContext? ctx))
        {
            ctx.PopulateKeyToMod(e.NameWithoutLocale);
        }
        if (e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
        {
            dataLocationReadyTick = Game1.ticks;
        }
    }

    internal static void AddItemTraceCtx(string itemTypeId, string assetNameStr)
    {
        itemTypeToTraceCtx[itemTypeId] = AddTraceCtx(help.GameContent.ParseAssetName(assetNameStr));
    }

    private static void AddTraceCtxForWallsAndFloors()
    {
        IAssetName assetName = help.GameContent.ParseAssetName("Data/AdditionalWallpaperFlooring");
        TraceContext ctx = new(
            assetName,
            static (ctx, itemId) =>
            {
                if (int.TryParse(itemId, out _))
                {
                    return ModNameInfo.STARDEW;
                }
                string[] parts = itemId.Split(':');
                if (parts.Length > 1 && int.TryParse(parts.Last(), out int idx))
                {
                    itemId = string.Join(':', parts.Take(parts.Length - 1));
                }
                if (ctx.KeyToMod.TryGetValue(itemId, out ModNameInfo? text))
                {
                    return text;
                }
                return null;
            }
        );
        itemTypeToTraceCtx["(WP)"] = ctx;
        itemTypeToTraceCtx["(FL)"] = ctx;
        traceCtx[assetName] = ctx;
    }

    private static TraceContext AddTraceCtx(string assetName)
    {
        return AddTraceCtx(help.GameContent.ParseAssetName(assetName));
    }

    internal static TraceContext AddTraceCtx(IAssetName assetName)
    {
        TraceContext ctx = new(assetName);
        traceCtx[ctx.TracedAsset] = ctx;
        return ctx;
    }

    private static void AddLocationEventTraceCtx(string locationId)
    {
        IAssetName assetName = help.GameContent.ParseAssetName($"Data/Events/{locationId}");
        if (traceCtx.ContainsKey(assetName))
            return;
        TraceContext ctx = new(
            assetName,
            static (ctx, eventId) =>
            {
                foreach ((string key, ModNameInfo? modName) in ctx.KeyToMod)
                {
                    if (Event.SplitPreconditions(key)[0] == eventId)
                        return modName;
                }
                return ModNameInfo.STARDEW;
            },
            isEvent: true
        );
        traceCtx[assetName] = ctx;
        help.GameContent.InvalidateCache(assetName);
    }

    private static void AssetRequestedEventArgs_Edit_Prefix(
        AssetRequestedEventArgs __instance,
        ref Action<IAssetData> apply,
        string? onBehalfOf
    )
    {
        foreach (TraceContext ctx in traceCtx.Values)
        {
            ctx.HandleEdit(__instance.AssetInfo, __instance.Mod, __instance.LoadOperations, ref apply, onBehalfOf);
        }
    }

    /// <summary>SMAPI static monitor Log wrapper</summary>
    /// <param name="msg"></param>
    /// <param name="level"></param>
    internal static void Log(string msg, LogLevel level = DEFAULT_LOG_LEVEL)
    {
        mon.Log(msg, level);
    }

    /// <summary>SMAPI static monitor LogOnce wrapper</summary>
    /// <param name="msg"></param>
    /// <param name="level"></param>
    internal static void LogOnce(string msg, LogLevel level = DEFAULT_LOG_LEVEL)
    {
        mon.LogOnce(msg, level);
    }

    /// <summary>SMAPI static monitor Log wrapper, debug only</summary>
    /// <param name="msg"></param>
    /// <param name="level"></param>
    [Conditional("DEBUG")]
    internal static void LogDebug(string msg, LogLevel level = DEFAULT_LOG_LEVEL)
    {
        mon.Log(msg, level);
    }
}
