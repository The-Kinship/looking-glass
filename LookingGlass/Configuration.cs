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

    /// <summary>Everything you typed, lower case, for searching.</summary>
    public string SearchText() => string.Join("\n", Type, MadeFor, Tags, Notes, Favorite ? "favorite favourite" : "").ToLowerInvariant();
}
