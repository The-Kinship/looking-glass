using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace LookingGlass;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Your Penumbra mod folder, only if it can't be found automatically ("" = find it from Penumbra's settings).</summary>
    public string ModFolderOverride { get; set; } = "";

    /// <summary>Your screenshot folder, only if it isn't the game's usual one ("" = Documents/My Games/FINAL FANTASY XIV - A Realm Reborn/screenshots).</summary>
    public string ScreenshotFolder { get; set; } = "";

    /// <summary>During a photo session, also use Glamourer to put on the item each mod replaces.</summary>
    public bool DressWithGlamourer { get; set; } = true;

    /// <summary>During a photo session, turn on every option in "pick several" groups (shirts, masks and other pieces).</summary>
    public bool AllPiecesOn { get; set; } = true;

    /// <summary>During a photo session, empty the clothing slots a gear mod doesn't use, so only the mod shows.</summary>
    public bool HideOtherClothes { get; set; } = true;

    /// <summary>Your own info for each mod, by the mod's folder name.</summary>
    public Dictionary<string, ModInfo> Info { get; set; } = new();

    /// <summary>Your info for a mod, or null if you haven't filled any in.</summary>
    public ModInfo? InfoFor(string dir) => Info.TryGetValue(dir, out var i) ? i : null;

    /// <summary>Your info for a mod, made empty the first time.</summary>
    public ModInfo EditInfo(string dir)
    {
        if (!Info.TryGetValue(dir, out var i)) { i = new ModInfo(); Info[dir] = i; }
        return i;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

/// <summary>What you've filled in about one mod.</summary>
[Serializable]
public class ModInfo
{
    public static readonly string[] Types =
        { "", "Gear", "Hair", "Body", "Tattoo", "Jewelry", "Emote", "FX", "Furniture", "Pet", "Other" };

    /// <summary>One of <see cref="Types"/> ("" = not set).</summary>
    public string Type { get; set; } = "";
    public bool Favorite { get; set; }
    /// <summary>Who it's made for, e.g. "Miqo'te male, TBSE".</summary>
    public string MadeFor { get; set; } = "";
    /// <summary>Your own tags, separated by commas.</summary>
    public string Tags { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>The preview picture's file name in the plugin's images folder ("" = none).</summary>
    public string ImageFile { get; set; } = "";
    /// <summary>Framing: how far it's zoomed in (1 = whole picture) and where the middle of the view is (0 to 1).</summary>
    public float Zoom { get; set; } = 1f;
    public float CenterX { get; set; } = 0.5f;
    public float CenterY { get; set; } = 0.5f;

    /// <summary>Everything you typed, lower case, for searching.</summary>
    public string SearchText() => string.Join("\n", Type, MadeFor, Tags, Notes, Favorite ? "favorite favourite" : "").ToLowerInvariant();
}
