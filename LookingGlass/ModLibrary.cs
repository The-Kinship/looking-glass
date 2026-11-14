using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LiteDB;

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
    /// <summary>Tags you gave it inside Penumbra (Penumbra's "local tags").</summary>
    public List<string> PenumbraTags { get; init; } = new();
    /// <summary>The note you wrote on it inside Penumbra.</summary>
    public string PenumbraNote { get; init; } = "";

    /// <summary>The name shown in Penumbra's list.</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>The Penumbra folders it sits in, e.g. "Gear/Tops" ("" when it's at the top).</summary>
    public string Folder { get; init; } = "";
    /// <summary>The top folder ("Gear" for "Gear/Tops").</summary>
    public string TopFolder => Folder.Contains('/') ? Folder[..Folder.IndexOf('/')] : Folder;

    /// <summary>True if it's in this folder or any folder inside it.</summary>
    public bool InFolder(string folder) =>
        string.Equals(Folder, folder, StringComparison.OrdinalIgnoreCase)
        || Folder.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>What Penumbra itself remembers about a mod: its folder, your Penumbra tags and note.</summary>
    private sealed class PenumbraData
    {
        public string Folder = "";
        public List<string> Tags = new();
        public string Note = "";
    }

    /// <summary>
    /// Penumbra 1.7 and newer keep each mod's folder, tags and note in mod_data.db (a LiteDB database).
    /// It's copied first and the copy is read, so Penumbra's own file is never opened or locked.
    /// </summary>
    private static Dictionary<string, PenumbraData>? ReadModData()
    {
        var root = ConfigsRoot();
        if (root == null) return null;
        var source = Path.Combine(root.FullName, "Penumbra", "mod_data.db");
        if (!File.Exists(source)) return null;

        var tempDir = Plugin.PluginInterface.ConfigDirectory.FullName;
        Directory.CreateDirectory(tempDir);
        var copy = Path.Combine(tempDir, "penumbra_copy.db");
        var copyLog = Path.Combine(tempDir, "penumbra_copy-log.db");
        try
        {
            CopyShared(source, copy);
            var log = Path.Combine(root.FullName, "Penumbra", "mod_data-log.db");
            if (File.Exists(log)) CopyShared(log, copyLog);
            else if (File.Exists(copyLog)) File.Delete(copyLog);

            var result = new Dictionary<string, PenumbraData>(StringComparer.OrdinalIgnoreCase);
            using (var db = new LiteDatabase($"Filename={copy};Connection=direct"))
            {
                foreach (var name in db.GetCollectionNames())
                {
                    foreach (var doc in db.GetCollection(name).FindAll())
                    {
                        if (!doc.TryGetValue("_id", out var id) || !id.IsString) continue;
                        if (!doc.TryGetValue("Folder", out var folder)) continue;
                        var data = new PenumbraData { Folder = folder.IsString ? folder.AsString : "" };
                        if (doc.TryGetValue("LocalTags", out var tags) && tags.IsArray)
                            foreach (var t in tags.AsArray) if (t.IsString && t.AsString.Length > 0) data.Tags.Add(t.AsString);
                        if (doc.TryGetValue("Note", out var note) && note.IsString) data.Note = note.AsString;
                        result[id.AsString] = data;
                    }
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not read Penumbra's mod_data.db.");
            return null;
        }
        finally
        {
            try { if (File.Exists(copy)) File.Delete(copy); } catch (Exception) { /* left for next time */ }
            try { if (File.Exists(copyLog)) File.Delete(copyLog); } catch (Exception) { /* left for next time */ }
        }
    }

    /// <summary>Copies a file even while Penumbra has it open.</summary>
    private static void CopyShared(string from, string to)
    {
        using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(to, FileMode.Create, FileAccess.Write);
        input.CopyTo(output);
    }

    /// <summary>Older Penumbra versions: the folder layout in sort_order.json (mod folder name to "Folder/Sub/Name").</summary>
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

        var modData = ReadModData();
        var sort = modData == null ? ReadSortOrder() : new Dictionary<string, string>();
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
                PenumbraData? pd = null;
                modData?.TryGetValue(dirName, out pd);
                string folderPath, display;
                if (pd != null) { folderPath = pd.Folder; display = name; }
                else if (sort.TryGetValue(dirName, out var p) && p.Length > 0)
                {
                    var cut = p.LastIndexOf('/');
                    folderPath = cut < 0 ? "" : p[..cut];
                    display = cut < 0 ? p : p[(cut + 1)..];
                }
                else { folderPath = ""; display = name; }
                var path = folderPath.Length > 0 ? folderPath + "/" + display : display;
                var penumbraTags = pd?.Tags ?? new List<string>();
                var penumbraNote = pd?.Note ?? "";
                var author = Str(m, "Author");
                var description = Str(m, "Description");
                list.Add(new ModEntry
                {
                    Dir = dirName,
                    MetaName = name,
                    PenumbraPath = path,
                    DisplayName = display,
                    Folder = folderPath,
                    Author = author,
                    Version = Str(m, "Version"),
                    Website = Str(m, "Website"),
                    Description = description,
                    Tags = tags,
                    PenumbraTags = penumbraTags,
                    PenumbraNote = penumbraNote,
                    SearchText = string.Join("\n", new[] { path, name, author, string.Join(" ", tags), string.Join(" ", penumbraTags), penumbraNote, description }).ToLowerInvariant(),
                });
            }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Skipped a mod with unreadable info: " + dirName); }
        }

        mods = list.OrderBy(x => x.PenumbraPath, StringComparer.OrdinalIgnoreCase).ToList();
        Status = $"{mods.Count} mods found." + (modData == null ? " (Couldn't read Penumbra's folders, so they're shown by name.)" : "");
    }
}
