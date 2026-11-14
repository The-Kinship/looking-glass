using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace LookingGlass;

/// <summary>The Looking Glass window: search and filter on top, the mod list on the left, the chosen mod on the right.</summary>
public sealed class MainWindow : Window
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.65f, 1f);

    private string search = "";
    private string folderFilter = "";   // "" = all folders
    private string selectedDir = "";
    private bool scrollToSelected;
    private string folderTyping = "";
    private string copiedNote = "";
    private DateTime copiedAt = DateTime.MinValue;
    /// <summary>Put the keyboard back in the search box next frame (after clicking a mod), so the arrow keys keep working.</summary>
    private bool focusSearch;
    /// <summary>True while the search box has the keyboard (the game ignores keys then, so arrows don't move your character).</summary>
    private bool searchActive;
    private string typeFilter = "";      // "" = all types, "(not set)" = no type picked yet
    private bool favoritesOnly;

    public MainWindow() : base("Rafter Cat's Looking Glass##LookingGlassMain")
    {
        Size = new Vector2(760, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480, 300), MaximumSize = new Vector2(4000, 4000) };
    }

    public override void OnOpen()
    {
        // Pick up mods added since last time.
        Plugin.Library.Refresh();
    }

    /// <summary>The mods that match the search and folder filter, in Penumbra order.</summary>
    private List<ModEntry> Filtered(List<ModEntry> all)
    {
        var words = search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cfg = Plugin.Config;
        return all.Where(m =>
            {
                if (folderFilter == "(top level)") { if (m.Folder.Length > 0) return false; }
                else if (folderFilter.Length > 0 && !m.InFolder(folderFilter)) return false;
                var info = cfg.InfoFor(m.Dir);
                if (favoritesOnly && info?.Favorite != true) return false;
                if (typeFilter == "(not set)" && (info?.Type ?? "").Length > 0) return false;
                if (typeFilter.Length > 0 && typeFilter != "(not set)" && info?.Type != typeFilter) return false;
                if (words.Length == 0) return true;
                var text = info == null ? m.SearchText : m.SearchText + "\n" + info.SearchText();
                return words.All(w => text.Contains(w));
            })
            .ToList();
    }

    public override void Draw()
    {
        var lib = Plugin.Library;
        var all = lib.Mods;

        // --- Top bar: search, folder filter, refresh ---
        if (focusSearch) { ImGui.SetKeyboardFocusHere(); focusSearch = false; }
        ImGui.SetNextItemWidth(Math.Max(120f, ImGui.GetContentRegionAvail().X - 330f));
        ImGui.InputText("Search##search", ref search, 100);
        searchActive = ImGui.IsItemActive();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Searches names, Penumbra folders, authors, tags and descriptions.\nSeveral words = all of them must match.\nWhile this box is active, the up and down arrows move through the list.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(170f);
        if (ImGui.BeginCombo("##folder", folderFilter.Length == 0 ? "All folders" : folderFilter))
        {
            if (ImGui.Selectable("All folders", folderFilter.Length == 0)) folderFilter = "";
            if (ImGui.Selectable("(top level)", folderFilter == "(top level)")) folderFilter = "(top level)";
            foreach (var f in AllFolders(all))
            {
                // Sub-folders are indented under their parent; picking one includes everything inside it.
                var depth = f.Count(ch => ch == '/');
                var label = new string(' ', depth * 4) + f[(f.LastIndexOf('/') + 1)..];
                if (ImGui.Selectable(label + "##f" + f, f == folderFilter)) folderFilter = f;
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button(lib.Loading ? "Loading..." : "Refresh") && !lib.Loading) lib.Refresh();

        // Second row: type filter and favorites
        ImGui.SetNextItemWidth(130f);
        if (ImGui.BeginCombo("##type", typeFilter.Length == 0 ? "All types" : typeFilter))
        {
            if (ImGui.Selectable("All types", typeFilter.Length == 0)) typeFilter = "";
            foreach (var t in ModInfo.Types.Where(t => t.Length > 0))
                if (ImGui.Selectable(t + "##t", t == typeFilter)) typeFilter = t;
            if (ImGui.Selectable("(not set)", typeFilter == "(not set)")) typeFilter = "(not set)";
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Favorites only", ref favoritesOnly);

        var shown = Filtered(all);
        ImGui.TextColored(Dim, lib.Loading || all.Count == 0 ? lib.Status
            : $"Showing {shown.Count} of {all.Count} mods. " + (searchActive ? "Arrow keys: up and down move through the list." : "Click a mod (or the search box) to use the arrow keys."));

        // --- Arrow keys move through the list, but only while the search box has the keyboard,
        //     because that's when the game stops reading keys (so your character doesn't walk). ---
        var index = shown.FindIndex(m => m.Dir == selectedDir);
        if (searchActive && shown.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) Step(shown, index, +1);
            if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) Step(shown, index, -1);
            if (ImGui.IsKeyPressed(ImGuiKey.PageDown)) Step(shown, index, +10);
            if (ImGui.IsKeyPressed(ImGuiKey.PageUp)) Step(shown, index, -10);
        }

        // --- Left: the list ---
        var bottom = 30f;
        var height = Math.Max(100f, ImGui.GetContentRegionAvail().Y - bottom);
        var listWidth = Math.Max(220f, ImGui.GetContentRegionAvail().X * 0.42f);
        if (ImGui.BeginChild("##list", new Vector2(listWidth, height)))
        {
            var starWidth = ImGui.GetFontSize() + 4f;
            foreach (var m in shown)
            {
                var isSel = m.Dir == selectedDir;
                var info = Plugin.Config.InfoFor(m.Dir);
                if (info?.Favorite == true) Star(Gold);
                else ImGui.Dummy(new Vector2(ImGui.GetFontSize(), ImGui.GetFontSize()));
                ImGui.SameLine(starWidth);
                if (ImGui.Selectable(m.DisplayName.Replace("##", "# #") + "##" + m.Dir, isSel)) { selectedDir = m.Dir; focusSearch = true; }
                if (ImGui.IsItemHovered())
                {
                    var tip = string.Join("\n", new[] { m.Folder, info?.Type ?? "", info?.MadeFor ?? "" }.Where(x => x.Length > 0));
                    if (tip.Length > 0) ImGui.SetTooltip(tip);
                }
                if (isSel && scrollToSelected) { ImGui.SetScrollHereY(0.5f); scrollToSelected = false; }
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // --- Right: the chosen mod ---
        if (ImGui.BeginChild("##details", new Vector2(0, height)))
        {
            var mod = all.FirstOrDefault(m => m.Dir == selectedDir);
            if (mod == null) ImGui.TextColored(Dim, "Pick a mod on the left.");
            else
            {
                if (ImGui.Button("< Previous")) { Step(shown, index, -1); focusSearch = true; }
                ImGui.SameLine();
                if (ImGui.Button("Next >")) { Step(shown, index, +1); focusSearch = true; }
                ImGui.Spacing();
                DrawDetails(mod);
            }
        }
        ImGui.EndChild();

        DrawSettings(lib);
    }

    /// <summary>Every Penumbra folder that has mods in it (and the folders above them), in order.</summary>
    private static List<string> AllFolders(List<ModEntry> all)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in all)
        {
            var f = m.Folder;
            while (f.Length > 0)
            {
                set.Add(f);
                var cut = f.LastIndexOf('/');
                f = cut < 0 ? "" : f[..cut];
            }
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Moves the selection up or down the shown list and scrolls to it.</summary>
    private void Step(List<ModEntry> shown, int index, int by)
    {
        if (shown.Count == 0) return;
        var next = index < 0 ? (by > 0 ? 0 : shown.Count - 1) : Math.Clamp(index + by, 0, shown.Count - 1);
        selectedDir = shown[next].Dir;
        scrollToSelected = true;
    }

    private void DrawDetails(ModEntry mod)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextColored(Gold, mod.DisplayName);
        ImGui.Spacing();

        ImGui.TextColored(Dim, "In Penumbra:");
        ImGui.SameLine();
        ImGui.TextUnformatted(mod.PenumbraPath);
        if (ImGui.Button("Copy name"))
        {
            ImGui.SetClipboardText(mod.DisplayName);
            copiedNote = "Copied! Paste it into Penumbra's mod search box.";
            copiedAt = DateTime.UtcNow;
        }
        ImGui.SameLine();
        if (ImGui.Button("Copy full path"))
        {
            ImGui.SetClipboardText(mod.PenumbraPath);
            copiedNote = "Copied the full folder path.";
            copiedAt = DateTime.UtcNow;
        }
        if ((DateTime.UtcNow - copiedAt).TotalSeconds < 4) ImGui.TextColored(Dim, copiedNote);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Dim, "[Preview image: coming in piece 3]");
        ImGui.Separator();
        ImGui.Spacing();

        DrawInfoCard(mod);

        if (mod.PenumbraTags.Count > 0) Row("Penumbra tags", string.Join(", ", mod.PenumbraTags));
        if (mod.PenumbraNote.Length > 0) Row("Penumbra note", mod.PenumbraNote);

        ImGui.Spacing();
        // The mod's own info stays folded away unless you open it.
        if (!ImGui.CollapsingHeader("From the mod itself"))
        {
            ImGui.PopTextWrapPos();
            return;
        }

        if (mod.MetaName != mod.DisplayName) Row("Original name", mod.MetaName);
        Row("Author", mod.Author);
        Row("Version", mod.Version);
        Row("Website", mod.Website);
        if (mod.Tags.Count > 0) Row("Mod tags", string.Join(", ", mod.Tags));
        if (mod.Description.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Description:");
            ImGui.TextUnformatted(mod.Description);
        }
        ImGui.PopTextWrapPos();
    }

    /// <summary>A star drawn with Dalamud's icon font.</summary>
    private static void Star(Vector4 color)
    {
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            ImGui.TextColored(color, FontAwesomeIcon.Star.ToIconString());
    }

    /// <summary>Your own info for this mod: favorite, type, who it's for, tags and notes. Saved as you go.</summary>
    private static void DrawInfoCard(ModEntry mod)
    {
        var cfg = Plugin.Config;
        var info = cfg.InfoFor(mod.Dir);
        var label = 110f;

        // Favorite
        var fav = info?.Favorite == true;
        if (ImGui.Checkbox("Favorite", ref fav)) { cfg.EditInfo(mod.Dir).Favorite = fav; cfg.Save(); }
        if (fav) { ImGui.SameLine(); Star(Gold); }

        // Type
        var type = info?.Type ?? "";
        ImGui.TextColored(Dim, "Type:");
        ImGui.SameLine(label);
        ImGui.SetNextItemWidth(160f);
        if (ImGui.BeginCombo("##ctype", type.Length == 0 ? "Choose..." : type))
        {
            foreach (var t in ModInfo.Types)
                if (ImGui.Selectable((t.Length == 0 ? "(not set)" : t) + "##ct", t == type)) { cfg.EditInfo(mod.Dir).Type = t; cfg.Save(); }
            ImGui.EndCombo();
        }

        TextField(mod.Dir, "Made for:", "##madefor", label, i => i.MadeFor, (i, v) => i.MadeFor = v, "e.g. Miqo'te male, TBSE");
        TextField(mod.Dir, "My tags:", "##tags", label, i => i.Tags, (i, v) => i.Tags = v, "e.g. long sleeves, laces, black");

        ImGui.TextColored(Dim, "Notes:");
        var notes = info?.Notes ?? "";
        if (ImGui.InputTextMultiline("##notes", ref notes, 2000, new Vector2(Math.Max(100f, ImGui.GetContentRegionAvail().X - 8f), 70f)))
            cfg.EditInfo(mod.Dir).Notes = notes;
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
    }

    /// <summary>One labelled text box on the info card. Saves when you click away or press Enter.</summary>
    private static void TextField(string dir, string title, string id, float labelWidth, Func<ModInfo, string> get, Action<ModInfo, string> set, string hint)
    {
        var cfg = Plugin.Config;
        var info = cfg.InfoFor(dir);
        var value = info == null ? "" : get(info);
        ImGui.TextColored(Dim, title);
        ImGui.SameLine(labelWidth);
        ImGui.SetNextItemWidth(Math.Max(100f, ImGui.GetContentRegionAvail().X - 8f));
        if (ImGui.InputText(id, ref value, 300)) set(cfg.EditInfo(dir), value);
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
        if (value.Length == 0 && !ImGui.IsItemActive() && ImGui.IsItemHovered()) ImGui.SetTooltip(hint);
    }

    private static void Row(string label, string value)
    {
        if (value.Length == 0) return;
        ImGui.TextColored(Dim, label + ":");
        ImGui.SameLine();
        ImGui.TextUnformatted(value);
    }

    /// <summary>Folded-up settings at the bottom: where your Penumbra mods are.</summary>
    private void DrawSettings(ModLibrary lib)
    {
        if (!ImGui.CollapsingHeader("Settings")) return;
        ImGui.TextColored(Dim, "Penumbra mod folder in use: " + (lib.ModFolder.Length > 0 ? lib.ModFolder : "(not found)"));
        ImGui.TextColored(Dim, "Only fill this in if your mods aren't found automatically:");
        if (folderTyping.Length == 0 && Plugin.Config.ModFolderOverride.Length > 0) folderTyping = Plugin.Config.ModFolderOverride;
        ImGui.SetNextItemWidth(Math.Max(200f, ImGui.GetContentRegionAvail().X - 120f));
        ImGui.InputText("##override", ref folderTyping, 400);
        ImGui.SameLine();
        if (ImGui.Button("Save and reload"))
        {
            Plugin.Config.ModFolderOverride = folderTyping.Trim();
            Plugin.Config.Save();
            lib.Refresh();
        }
    }
}
