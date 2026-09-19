using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Sickhead.Engine.Util;
using StardewModdingAPI;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Framework.Content;

namespace ModNameTooltip;

public sealed record DataTraceFrame(string? EditedBy, string? OnBehalfOf, IReadOnlySet<string> AddedKeys)
{
    public string ModId => OnBehalfOf ?? EditedBy ?? "UNKNOWN";
}

public sealed class TraceContext(
    IAssetName tracedAsset,
    Func<TraceContext, string, ModNameInfo?>? specialLookup = null,
    bool isEvent = false
)
{
    public readonly IAssetName TracedAsset = tracedAsset;
    private readonly Func<TraceContext, string, ModNameInfo?>? specialLookup = specialLookup;
    internal readonly bool isEvent = isEvent;

    internal bool active = true;
    internal bool editing = false;
    private HashSet<string>? tracedKeys = null;
    private readonly List<DataTraceFrame> tracedFrames = [];

    internal static Dictionary<Type, Delegate?> keyGetters = [];
    internal static Dictionary<Type, Delegate?> idGetters = [];

    private readonly Dictionary<string, ModNameInfo> keyToMod = [];
    public IReadOnlyDictionary<string, ModNameInfo> KeyToMod => keyToMod;

    public bool TryGetModName(string key, [NotNullWhen(true)] out ModNameInfo? modName)
    {
        modName = specialLookup?.Invoke(this, key);
        if (modName != null)
            return true;
        return KeyToMod.TryGetValue(key, out modName);
    }

    public void PopulateKeyToMod(IAssetName assetName)
    {
        if (!active || editing || !TracedAsset.IsEquivalentTo(assetName) || tracedKeys == null)
            return;

        // intentionally do not clear the keyToMod
        foreach (DataTraceFrame frame in tracedFrames)
        {
            string modId = frame.ModId;
            ModNameInfo modNameText = ModNameInfo.Make(modId);
            foreach (string key in frame.AddedKeys)
            {
                keyToMod[key] = modNameText;
            }
        }

        tracedKeys = null;
        tracedFrames.Clear();
    }

    public void HandleEdit(
        IAssetInfo asset,
        IModMetadata? mod,
        List<AssetLoadOperation> loadOperations,
        ref Action<IAssetData> apply,
        string? onBehalfOf = null
    )
    {
        if (!active)
            return;
        if (editing)
            return;
        if (!TracedAsset.IsEquivalentTo(asset.NameWithoutLocale))
            return;
        if (!asset.DataType.IsGenericType)
            return;
        Type genericDef = asset.DataType.GetGenericTypeDefinition();
        Type[] genericArgs = asset.DataType.GetGenericArguments();
        if (genericDef != typeof(List<>) && (genericDef != typeof(Dictionary<,>) || genericArgs[0] != typeof(string)))
            return;
        HandleEdit_TraceKindData(asset, mod, loadOperations, ref apply, onBehalfOf);
    }

    private void HandleEdit_TraceKindData(
        IAssetInfo asset,
        IModMetadata? mod,
        List<AssetLoadOperation> loadOperations,
        ref Action<IAssetData> apply,
        string? onBehalfOf
    )
    {
        Delegate? hashGetter = GetOrCreateKeyGetter(asset.DataType);
        if (hashGetter == null)
            return;
        Action<IAssetData> originalApply = apply;
        apply = asset =>
        {
            if (!active || editing)
            {
                // original
                originalApply(asset);
                // original
                return;
            }

            if (tracedKeys == null)
            {
                tracedKeys = (HashSet<string>?)hashGetter.DynamicInvoke(asset);
                if (tracedKeys == null)
                {
                    ModEntry.Log($"Failed to get traced keys for '{TracedAsset}', disabling tracking", LogLevel.Warn);
                    active = false;
                    // original
                    originalApply(asset);
                    // original
                    return;
                }
                AssetLoadOperation? loader = loadOperations.MaxBy(p => p.Priority);
                tracedFrames.Add(
                    new DataTraceFrame(
                        loader?.Mod.Manifest.UniqueID ?? ModNameInfo.STARDEW_VALLEY,
                        loader?.OnBehalfOf?.Manifest.UniqueID
                            ?? loader?.Mod.Manifest.UniqueID
                            ?? ModNameInfo.STARDEW_VALLEY,
                        tracedKeys.ToHashSet()
                    )
                );
            }

            // original
            editing = true;
            originalApply(asset);
            editing = false;
            // original

            HashSet<string>? tracedKeysAfter = (HashSet<string>?)hashGetter.DynamicInvoke(asset);
            if (tracedKeysAfter == null)
            {
                ModEntry.Log($"Failed to get traced keys for '{TracedAsset}', disabling tracking", LogLevel.Warn);
                active = false;
                return;
            }

            HashSet<string> added = tracedKeysAfter.Except(tracedKeys).ToHashSet();
            tracedKeys = tracedKeysAfter;

            tracedFrames.Add(new DataTraceFrame(mod?.Manifest.UniqueID, onBehalfOf, added));
        };
    }

    private static Delegate? GetOrCreateKeyGetter(Type typ)
    {
        if (!keyGetters.TryGetValue(typ, out Delegate? methodInfo))
        {
            methodInfo = CreateKeyGetter(typ);
            keyGetters[typ] = methodInfo;
        }
        return methodInfo;
    }

    private static Delegate? CreateKeyGetter(Type typ)
    {
        Type genericDef = typ.GetGenericTypeDefinition();
        Type[] genericArgs = typ.GetGenericArguments();
        if (genericDef == typeof(Dictionary<,>) && genericArgs[0] == typeof(string))
        {
            return CheckStringDictInfo
                ?.MakeGenericMethod(genericArgs[1])
                .CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(IAssetData), typeof(HashSet<string>)));
        }
        else if (genericDef == typeof(List<>))
        {
            return CheckIdListInfo
                ?.MakeGenericMethod(genericArgs[0])
                .CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(IAssetData), typeof(HashSet<string>)));
        }
        return null;
    }

    private static readonly MethodInfo? CheckStringDictInfo = typeof(TraceContext).GetMethod(
        nameof(CheckStringDict),
        BindingFlags.Static | BindingFlags.NonPublic
    );

    private static HashSet<string> CheckStringDict<TValue>(IAssetData asset)
    {
        IDictionary<string, TValue> data = asset.AsDictionary<string, TValue>().Data;
        HashSet<string> keySet = [];
        foreach ((string key, TValue value) in data)
        {
            if (value != null)
                keySet.Add(key);
        }
        return keySet;
    }

    private static readonly MethodInfo? CheckIdListInfo = typeof(TraceContext).GetMethod(
        nameof(CheckIdList),
        BindingFlags.Static | BindingFlags.NonPublic
    );

    private static HashSet<string> CheckIdList<TValue>(IAssetData asset)
    {
        Delegate? getId = GetIdGetter(typeof(TValue));
        if (getId == null)
            return [];

        IList<TValue> data = asset.GetData<IList<TValue>>();
        HashSet<string> result = [];
        foreach (TValue item in data)
        {
            result.Add((string)getId.DynamicInvoke(item)!);
        }
        return result;
    }

    private static Delegate? GetIdGetter(Type typ)
    {
        if (!idGetters.TryGetValue(typ, out Delegate? idGetter))
        {
            idGetter = MakeIdGetter(typ);
            idGetters[typ] = idGetter;
        }
        return idGetter;
    }

    private static Delegate? MakeIdGetter(Type typ)
    {
        if (
            (typ.GetProperty("Id") ?? typ.GetProperty("ID")) is PropertyInfo propInfo
            && propInfo.GetDataType() == typeof(string)
        )
        {
            return propInfo.GetGetMethod()?.CreateDelegate(typeof(Func<,>).MakeGenericType(typ, typeof(string)));
        }
        else if (
            (typ.GetField("Id") ?? typ.GetField("ID")) is FieldInfo fieldInfo
            && fieldInfo.GetDataType() == typeof(string)
        )
        {
            return (object thing) => (string)fieldInfo.GetValue(thing)!;
        }
        return null;
    }
}
