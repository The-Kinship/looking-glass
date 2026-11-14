using System;
using Dalamud.Configuration;

namespace LookingGlass;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Your Penumbra mod folder, only if it can't be found automatically ("" = find it from Penumbra's settings).</summary>
    public string ModFolderOverride { get; set; } = "";

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
