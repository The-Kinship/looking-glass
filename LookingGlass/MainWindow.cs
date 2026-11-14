using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
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
        return all.Where(m =>
                (folderFilter.Length == 0 || string.Equals(m.TopFolder, folderFilter, StringComparison.OrdinalIgnoreCase)
                    || (folderFilter == "(top level)" && m.TopFolder.Length == 0))
                && words.All(w => m.SearchText.Contains(w)))
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
        ImGui.SetNextItemWidth(150f);
        if (ImGui.BeginCombo("##folder", folderFilter.Length == 0 ? "All folders" : folderFilter))
        {
            if (ImGui.Selectable("All folders", folderFilter.Length == 0)) folderFilter = "";
            foreach (var f in all.Select(m => m.TopFolder.Length == 0 ? "(top level)" : m.TopFolder).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                if (ImGui.Selectable(f + "##f", f == folderFilter)) folderFilter = f;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button(lib.Loading ? "Loading..." : "Refresh") && !lib.Loading) lib.Refresh();

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
            foreach (var m in shown)
            {
                var isSel = m.Dir == selectedDir;
                if (ImGui.Selectable(m.DisplayName.Replace("##", "# #") + "##" + m.Dir, isSel)) { selectedDir = m.Dir; focusSearch = true; }
                if (ImGui.IsItemHovered() && m.Folder.Length > 0) ImGui.SetTooltip(m.Folder);
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

        if (mod.MetaName != mod.DisplayName) Row("Original name", mod.MetaName);
        Row("Author", mod.Author);
        Row("Version", mod.Version);
        Row("Website", mod.Website);
        if (mod.Tags.Count > 0) Row("Penumbra tags", string.Join(", ", mod.Tags));
        if (mod.Description.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Dim, "Description:");
            ImGui.TextUnformatted(mod.Description);
        }
        ImGui.PopTextWrapPos();
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
