using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LookingGlass;

/// <summary>One installed Penumbra mod, as read from its folder.</summary>
public sealed class ModEntry
{
    /// <summary>The mod's folder name inside the Penumbra mod folder (Penumbra's own ID for it).</summary>
    public string Dir { get; init; } = "";
    /// <summary>The name in the mod's own info (meta.json).</summary>
    public string MetaName { get; init; } = "";
    /// <summary>Where it sits in your Penumbra list, e.g. "Gear/Tops/Laced Sleeves".</summary>
    public string PenumbraPath { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Website { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Tags { get; init; } = new();

    /// <summary>The name shown in Penumbra's list (the last part of the path).</summary>
    public string DisplayName => PenumbraPath.Contains('/') ? PenumbraPath[(PenumbraPath.LastIndexOf('/') + 1)..] : PenumbraPath;
    /// <summary>The folders it sits in ("" when it's at the top).</summary>
    public string Folder => PenumbraPath.Contains('/') ? PenumbraPath[..PenumbraPath.LastIndexOf('/')] : "";
    /// <summary>The top folder ("Gear" for "Gear/Tops/...").</summary>
    public string TopFolder => PenumbraPath.Contains('/') ? PenumbraPath[..PenumbraPath.IndexOf('/')] : "";

    /// <summary>Everything searchable, lower case.</summary>
    public string SearchText { get; init; } = "";
}

/// <summary>
/// Reads your mods straight from Penumbra's files (read only): the mod folder set in Penumbra's settings,
/// each mod's meta.json, and Penumbra's sort_order.json for your folder layout.
/// </summary>
public sealed class ModLibrary
{
    private volatile List<ModEntry> mods = new();
    public List<ModEntry> Mods => mods;

    public bool Loading { get; private set; }
    /// <summary>A short line about the last load (problems or how many were found).</summary>
    public string Status { get; private set; } = "";
    /// <summary>The mod folder that was used ("" if none was found).</summary>
    public string ModFolder { get; private set; } = "";

    /// <summary>The folder with every plugin's settings (pluginConfigs), found from this plugin's own settings folder.</summary>
    private static DirectoryInfo? ConfigsRoot()
    {
        try { return Plugin.PluginInterface.ConfigDirectory.Parent; } catch (Exception) { return null; }
    }

    private static string FindModFolder()
    {
        var manual = Plugin.Config.ModFolderOverride.Trim();
        if (manual.Length > 0) return manual;
        var root = ConfigsRoot();
        if (root == null) return "";
        var file = Path.Combine(root.FullName, "Penumbra.json");
        if (!File.Exists(file)) return "";
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.TryGetProperty("ModDirectory", out var d) ? d.GetString() ?? "" : "";
    }

    /// <summary>Penumbra's folder layout: mod folder name to "Folder/Sub/Name".</summary>
    private static Dictionary<string, string> ReadSortOrder()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var root = ConfigsRoot();
            if (root == null) return result;
            var file = Path.Combine(root.FullName, "Penumbra", "sort_order.json");
            if (!File.Exists(file)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.Object)
                foreach (var p in data.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) result[p.Name] = p.Value.GetString() ?? "";
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not read Penumbra's sort order."); }
        return result;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Reads everything again in the background.</summary>
    public void Refresh()
    {
        if (Loading) return;
        Loading = true;
        Status = "Loading your mods...";
        Task.Run(() =>
        {
            try { Load(); }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Loading mods failed.");
                Status = "Couldn't read your mods: " + ex.Message;
            }
            finally { Loading = false; }
        });
    }

    private void Load()
    {
        var folder = FindModFolder();
        ModFolder = folder;
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            mods = new();
            Status = folder.Length == 0
                ? "Couldn't find your Penumbra mod folder. Set it in Settings below."
                : "Your Penumbra mod folder doesn't exist: " + folder;
            return;
        }

        var sort = ReadSortOrder();
        var list = new List<ModEntry>();
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            var metaFile = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaFile)) continue;
            var dirName = Path.GetFileName(dir);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metaFile));
                var m = doc.RootElement;
                var name = Str(m, "Name");
                if (name.Length == 0) name = dirName;
                var tags = new List<string>();
                if (m.TryGetProperty("ModTags", out var t) && t.ValueKind == JsonValueKind.Array)
                    foreach (var tag in t.EnumerateArray())
                        if (tag.ValueKind == JsonValueKind.String && (tag.GetString() ?? "").Length > 0) tags.Add(tag.GetString()!);
                var path = sort.TryGetValue(dirName, out var p) && p.Length > 0 ? p : name;
                var author = Str(m, "Author");
                var description = Str(m, "Description");
                list.Add(new ModEntry
                {
                    Dir = dirName,
                    MetaName = name,
                    PenumbraPath = path,
                    Author = author,
                    Version = Str(m, "Version"),
                    Website = Str(m, "Website"),
                    Description = description,
                    Tags = tags,
                    SearchText = string.Join("\n", new[] { path, name, author, string.Join(" ", tags), description }).ToLowerInvariant(),
                });
            }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Skipped a mod with unreadable info: " + dirName); }
        }

        mods = list.OrderBy(x => x.PenumbraPath, StringComparer.OrdinalIgnoreCase).ToList();
        Status = $"{mods.Count} mods found.";
    }
}
