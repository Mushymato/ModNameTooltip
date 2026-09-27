using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Sickhead.Engine.Util;
using StardewModdingAPI;
using StardewModdingAPI.Framework;
using StardewModdingAPI.Framework.Content;

namespace ModNameTooltip;

internal interface ITraceContext
{
    public IAssetName TracedAsset { get; }
    public IReadOnlyDictionary<string, ModNameInfo> KeyToMod { get; }
    public bool Active { get; set; }
    public bool IsEvent { get; }

    public bool TryGetModName(string key, [NotNullWhen(true)] out ModNameInfo? modName);
    public void AssetDoneEdit(IAssetName assetName);
    public void HandleEdit(
        IAssetInfo asset,
        IModMetadata? mod,
        List<AssetLoadOperation> loadOperations,
        ref Action<IAssetData> apply,
        string? onBehalfOf = null
    );
}

internal sealed class TraceContext<TValue>(
    IAssetName tracedAsset,
    bool isList,
    Func<ITraceContext, string, ModNameInfo?>? specialLookup = null,
    bool isEvent = false
) : ITraceContext
{
    public IAssetName TracedAsset { get; } = tracedAsset;
    private readonly Func<ITraceContext, string, ModNameInfo?>? specialLookup = specialLookup;
    public bool IsEvent { get; } = isEvent;
    private readonly bool isList = isList;
    private readonly Type dataType = GetDataType(isList);

    private static Type GetDataType(bool isList)
    {
        if (isList)
            return typeof(List<TValue>);
        return typeof(Dictionary<string, TValue>);
    }

    public bool Active { get; set; } = true;
    internal bool editing = false;
    private HashSet<string>? tracedKeys = null;

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

    public void AssetDoneEdit(IAssetName assetName)
    {
        if (!Active || editing || !TracedAsset.IsEquivalentTo(assetName) || tracedKeys == null)
            return;

        tracedKeys = null;
    }

    public void HandleEdit(
        IAssetInfo asset,
        IModMetadata? mod,
        List<AssetLoadOperation> loadOperations,
        ref Action<IAssetData> apply,
        string? onBehalfOf = null
    )
    {
        if (!Active)
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
        if (asset.DataType != dataType)
        {
            ModEntry.Log(
                $"Unexpected datatype for '{TracedAsset}' ({asset.DataType} != {dataType}), disabling tracking",
                LogLevel.Warn
            );
            Active = false;
            return;
        }

        Action<IAssetData> originalApply = apply;
        apply = asset =>
        {
            if (!Active || editing)
            {
                // original
                originalApply(asset);
                // original
                return;
            }

            if (tracedKeys == null)
            {
                AssetLoadOperation? loader = loadOperations.MaxBy(p => p.Priority);
                CheckAsset(
                    asset,
                    ModNameInfo.Make(
                        loader?.OnBehalfOf?.Manifest.UniqueID
                            ?? loader?.Mod.Manifest.UniqueID
                            ?? ModNameInfo.STARDEW_VALLEY
                    )
                );
                if (tracedKeys == null)
                {
                    ModEntry.Log($"Failed to get traced keys for '{TracedAsset}', disabling tracking", LogLevel.Warn);
                    Active = false;
                    // original
                    originalApply(asset);
                    // original
                    return;
                }
            }

            // original
            editing = true;
            originalApply(asset);
            editing = false;
            // original

            CheckAsset(asset, ModNameInfo.Make(onBehalfOf ?? mod?.Manifest.UniqueID ?? string.Empty));
        };
    }

    private void CheckAsset(IAssetData asset, ModNameInfo info)
    {
        tracedKeys = isList
            ? CheckIdList(asset, info, keyToMod, tracedKeys)
            : CheckStringDict(asset, info, keyToMod, tracedKeys);
    }

    private static HashSet<string> CheckStringDict(
        IAssetData asset,
        ModNameInfo info,
        Dictionary<string, ModNameInfo> keyToMod,
        HashSet<string>? tracedKeys
    )
    {
        IDictionary<string, TValue> data = asset.AsDictionary<string, TValue>().Data;
        tracedKeys ??= [];
        foreach ((string key, TValue value) in data)
        {
            if (value != null && tracedKeys.Add(key))
            {
                keyToMod[key] = info;
            }
        }
        return tracedKeys;
    }

    private static HashSet<string> CheckIdList(
        IAssetData asset,
        ModNameInfo info,
        Dictionary<string, ModNameInfo> keyToMod,
        HashSet<string>? tracedKeys
    )
    {
        Delegate? getId = GetIdGetter(typeof(TValue));
        if (getId == null)
            return [];

        IList<TValue> data = asset.GetData<IList<TValue>>();
        tracedKeys ??= [];
        foreach (TValue item in data)
        {
            if (item == null)
                continue;
            string? id = (string?)getId.DynamicInvoke(item);
            if (id != null && tracedKeys.Add(id))
            {
                keyToMod[id] = info;
            }
        }
        return tracedKeys;
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
