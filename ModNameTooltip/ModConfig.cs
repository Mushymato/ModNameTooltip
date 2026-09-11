using Microsoft.Xna.Framework;
using ModNameTooltip.Features;
using ModNameTooltip.Integration;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace ModNameTooltip;

public sealed class ModConfig
{
    public bool Enable_Tooltip { get; set; } = true;
    public KeybindList Toggle_Tooltip = new();
    public bool Enable_HUD { get; set; } = true;
    public KeybindList Toggle_HUD = new();
    public KeybindList HoldToShow_HUD = new();
    public bool Enable_HUD_NPC { get; set; } = true;
    public bool Enable_HUD_FarmAnimal { get; set; } = true;
    public bool Enable_HUD_Object { get; set; } = true;
    public bool Enable_HUD_TerrainFeature { get; set; } = true;
    public bool Enable_HUD_Building { get; set; } = true;
    public bool Enable_HUD_Event { get; set; } = false;
    public bool Enable_HUD_Location { get; set; } = false;
    public bool Display_ModId { get; set; } = false;
    public bool Display_ModSource { get; set; } = false;
    public string? Color_SDV
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                if (string.IsNullOrEmpty(field))
                    Color_SDV_Parsed = null;
                else
                    Color_SDV_Parsed = Utility.StringToColor(field);
            }
        }
    } = null;
    public string? Color_Mod
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                if (string.IsNullOrEmpty(field))
                    Color_Mod_Parsed = null;
                else
                    Color_Mod_Parsed = Utility.StringToColor(field);
            }
        }
    } = null;

    internal Color? Color_SDV_Parsed = null;
    internal Color? Color_Mod_Parsed = null;

    internal bool HoldingToShow = false;
    internal bool Enable_HUD_Display => (HoldingToShow || !HoldToShow_HUD.IsBound) && Enable_HUD;

    public void Register(IManifest mod, IGenericModConfigMenuApi? gmcm)
    {
        if (gmcm == null)
            return;
        gmcm.Register(mod, Reset, Save);
        gmcm.AddBoolOption(
            mod,
            () => Enable_Tooltip,
            SetEnable_Tooltip,
            I18n.Config_EnableTooltip_Name,
            I18n.Config_EnableTooltip_Desc
        );
        gmcm.AddKeybindList(
            mod,
            () => Toggle_Tooltip,
            (value) => Toggle_Tooltip = value,
            I18n.Config_ToggleTooltip_Name,
            I18n.Config_ToggleTooltip_Desc
        );
        gmcm.AddBoolOption(
            mod,
            () => Enable_HUD,
            SetEnable_HUD,
            I18n.Config_EnableHUD_Name,
            I18n.Config_EnableHUD_Desc
        );
        gmcm.AddKeybindList(
            mod,
            () => Toggle_HUD,
            (value) => Toggle_HUD = value,
            I18n.Config_ToggleHUD_Name,
            I18n.Config_ToggleHUD_Desc
        );
        gmcm.AddKeybindList(
            mod,
            () => HoldToShow_HUD,
            (value) => HoldToShow_HUD = value,
            I18n.Config_HoldToShowHUD_Name,
            I18n.Config_HoldToShowHUD_Desc
        );
        gmcm.AddBoolOption(mod, () => Enable_HUD_NPC, (value) => Enable_HUD_NPC = value, I18n.Config_EnableHUDNPC_Name);
        gmcm.AddBoolOption(
            mod,
            () => Enable_HUD_FarmAnimal,
            (value) => Enable_HUD_FarmAnimal = value,
            I18n.Config_EnableHUDFarmAnimal_Name
        );
        gmcm.AddBoolOption(
            mod,
            () => Enable_HUD_Object,
            (value) => Enable_HUD_Object = value,
            I18n.Config_EnableHUDObject_Name
        );
        gmcm.AddBoolOption(
            mod,
            () => Enable_HUD_TerrainFeature,
            (value) => Enable_HUD_TerrainFeature = value,
            I18n.Config_EnableHUDTerrainFeature_Name
        );
        gmcm.AddBoolOption(
            mod,
            () => Enable_HUD_Building,
            (value) => Enable_HUD_Building = value,
            I18n.Config_EnableHUDBuilding_Name
        );
        gmcm.AddBoolOption(
            mod,
            () => Enable_HUD_Event,
            (value) => Enable_HUD_Event = value,
            I18n.Config_EnableHUDEvent_Name
        );
        gmcm.AddBoolOption(
            mod,
            () => Enable_HUD_Location,
            (value) => Enable_HUD_Location = value,
            I18n.Config_EnableHUDLocation_Name
        );
        gmcm.AddBoolOption(mod, () => Display_ModId, (value) => Display_ModId = value, I18n.Config_DisplayModId_Name);
        gmcm.AddBoolOption(
            mod,
            () => Display_ModSource,
            (value) => Display_ModSource = value,
            I18n.Config_DisplayModSource_Name
        );
        gmcm.AddTextOption(
            mod,
            () => Color_SDV ?? string.Empty,
            (value) => Color_SDV = value,
            I18n.Config_ColorSDV_Name,
            I18n.Config_ColorSDV_Desc
        );
        gmcm.AddTextOption(
            mod,
            () => Color_Mod ?? string.Empty,
            (value) => Color_Mod = value,
            I18n.Config_ColorMod_Name,
            I18n.Config_ColorMod_Desc
        );
    }

    internal void SetEnable_Tooltip(bool value)
    {
        if (Enable_Tooltip != value)
        {
            Enable_Tooltip = value;
            Patch_HoverText.Toggle();
        }
    }

    internal void SetEnable_HUD(bool value)
    {
        if (Enable_HUD != value)
            Enable_HUD = value;
    }

    internal void SetHoldingToShow(bool value)
    {
        if (HoldingToShow != value)
            HoldingToShow = value;
    }

    private void Reset()
    {
        ModConfig defaultConfig = new();
        Enable_Tooltip = defaultConfig.Enable_Tooltip;
        Toggle_Tooltip = defaultConfig.Toggle_Tooltip;
        Enable_HUD = defaultConfig.Enable_HUD;
        Toggle_HUD = defaultConfig.Toggle_HUD;
        Enable_HUD_NPC = defaultConfig.Enable_HUD_NPC;
        Enable_HUD_FarmAnimal = defaultConfig.Enable_HUD_FarmAnimal;
        Enable_HUD_Object = defaultConfig.Enable_HUD_Object;
        Enable_HUD_TerrainFeature = defaultConfig.Enable_HUD_TerrainFeature;
        Enable_HUD_Building = defaultConfig.Enable_HUD_Building;
        Enable_HUD_Event = defaultConfig.Enable_HUD_Event;
        Enable_HUD_Location = defaultConfig.Enable_HUD_Location;
        Display_ModId = defaultConfig.Display_ModId;
        Display_ModSource = defaultConfig.Display_ModSource;
        Color_SDV = defaultConfig.Color_SDV;
        Color_Mod = defaultConfig.Color_Mod;
    }

    private void Save()
    {
        ModEntry.help.WriteConfig(this);
    }
}
