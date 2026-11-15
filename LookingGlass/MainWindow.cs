using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
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
    private int pictureFilter;   // 0 = all, 1 = with a picture, 2 = no picture yet
    private static readonly string[] PictureFilterNames = { "With or without pictures", "With a picture", "No picture yet" };
    private readonly FileDialogManager fileDialog = new();
    private string imageMessage = "";
    private string imageMessageFor = "";
    private string shotFolderTyping = "";

    public MainWindow() : base("Rafter Cat's Looking Glass##LookingGlassMain")
    {
        Size = new Vector2(760, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(480, 300), MaximumSize = new Vector2(4000, 4000) };
    }

    public override void OnClose() => PhotoSession.End();

    public override void OnOpen()
    {
        PenumbraIpc.ForgetChangedItems();
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
                var hasPicture = (info?.ImageFile.Length ?? 0) > 0;
                if (pictureFilter == 1 && !hasPicture) return false;
                if (pictureFilter == 2 && hasPicture) return false;
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
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("##pics", PictureFilterNames[pictureFilter]))
        {
            for (var i = 0; i < PictureFilterNames.Length; i++)
                if (ImGui.Selectable(PictureFilterNames[i], i == pictureFilter)) pictureFilter = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.Checkbox("Favorites only", ref favoritesOnly);

        var shown = Filtered(all);
        ImGui.TextColored(Dim, lib.Loading || all.Count == 0 ? lib.Status
            : $"Showing {shown.Count} of {all.Count} mods. " + (searchActive ? "Arrow keys: up and down move through the list." : "Click a mod (or the search box) to use the arrow keys."));

        if (PhotoSession.Active)
        {
            ImGui.TextColored(Gold, "Photo session is on: the mod you pick is put on you (only until you move on).");
            ImGui.SameLine();
            if (ImGui.SmallButton("End photo session")) PhotoSession.End();
        }

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

        // Photo session: put the chosen mod on (takes the last one off first).
        if (PhotoSession.Active && selectedDir.Length > 0) PhotoSession.Show(selectedDir);

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
        fileDialog.Draw();
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
        if (ImGui.Button("Show in Penumbra"))
        {
            copiedNote = PenumbraIpc.Open(mod.Dir) ? "Opened in Penumbra." : PenumbraIpc.LastError;
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
        DrawSessionBox(mod);
        DrawPreview(mod);
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

    /// <summary>The preview picture (framed to your zoom), with buttons to add, replace or remove it.</summary>
    private void DrawPreview(ModEntry mod)
    {
        var info = Plugin.Config.InfoFor(mod.Dir);
        var wrap = Previews.Get(info);
        var boxW = Math.Max(100f, ImGui.GetContentRegionAvail().X - 8f);
        var boxH = Math.Min(380f, boxW * 0.75f);

        if (wrap != null && info != null)
        {
            // The part of the picture to show: a window 1/zoom the size of the picture, centred where you set it.
            var z = Math.Clamp(info.Zoom, 1f, 6f);
            var half = 0.5f / z;
            var cx = Math.Clamp(info.CenterX, half, 1f - half);
            var cy = Math.Clamp(info.CenterY, half, 1f - half);
            var uv0 = new Vector2(cx - half, cy - half);
            var uv1 = new Vector2(cx + half, cy + half);
            var aspect = (float)wrap.Width / Math.Max(1, wrap.Height);
            var size = aspect > boxW / boxH ? new Vector2(boxW, boxW / aspect) : new Vector2(boxH * aspect, boxH);
            ImGui.Image(wrap.Handle, size, uv0, uv1);
        }
        else if (info != null && info.ImageFile.Length > 0)
        {
            ImGui.Dummy(new Vector2(boxW, 40f));
            ImGui.TextColored(Dim, "Loading picture...");
        }
        else
        {
            ImGui.TextColored(Dim, "No preview yet. Put the mod on, take a screenshot, then press \"Use my last screenshot\".");
        }

        if (ImGui.Button(info?.ImageFile.Length > 0 ? "Replace with my last screenshot" : "Use my last screenshot"))
        {
            var shot = Previews.LatestScreenshot();
            ShowImageMessage(mod.Dir, shot == null
                ? "No screenshots found in " + Previews.ScreenshotDir() + " (you can change the folder in Settings)."
                : Previews.Attach(mod.Dir, shot.FullName) ?? $"Added {shot.Name} (taken {shot.LastWriteTime:t}).");
        }
        if (ImGui.IsItemHovered())
        {
            var shot = Previews.LatestScreenshot();
            ImGui.SetTooltip(shot == null ? "No screenshots found yet." : $"Your newest screenshot: {shot.Name}\nTaken {shot.LastWriteTime:g}");
        }
        ImGui.SameLine();
        if (ImGui.Button("Pick a picture file..."))
        {
            var dir = mod.Dir;
            fileDialog.OpenFileDialog("Pick a preview picture", "Pictures{.png,.jpg,.jpeg,.bmp}", (ok, path) =>
            {
                if (ok) ShowImageMessage(dir, Previews.Attach(dir, path) ?? "Picture added.");
            });
        }
        if (info?.ImageFile.Length > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Remove picture")) { Previews.Remove(mod.Dir); ShowImageMessage(mod.Dir, "Picture removed."); }
        }
        if (imageMessageFor == mod.Dir && imageMessage.Length > 0) ImGui.TextColored(Dim, imageMessage);

        // Framing: zoom in on your character and move the view around.
        if (wrap != null && info != null && ImGui.TreeNode("Adjust framing"))
        {
            var cfg = Plugin.Config;
            var zoom = info.Zoom;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.SliderFloat("Zoom", ref zoom, 1f, 6f, "%.1fx")) info.Zoom = zoom;
            if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
            var x = info.CenterX;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.SliderFloat("Left / right", ref x, 0f, 1f, "%.2f")) info.CenterX = x;
            if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
            var y = info.CenterY;
            ImGui.SetNextItemWidth(200f);
            if (ImGui.SliderFloat("Up / down", ref y, 0f, 1f, "%.2f")) info.CenterY = y;
            if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
            if (ImGui.Button("Reset framing")) { info.Zoom = 1f; info.CenterX = 0.5f; info.CenterY = 0.5f; cfg.Save(); }
            ImGui.TreePop();
        }
    }

    /// <summary>Photo session controls and what the mod replaces (so you know what to wear for it to show).</summary>
    private static void DrawSessionBox(ModEntry mod)
    {
        if (!PhotoSession.Active)
        {
            if (ImGui.Button("Start photo session"))
                PhotoSession.Start();
            ImGui.SameLine();
            var dress = Plugin.Config.DressWithGlamourer;
            if (ImGui.Checkbox("Also dress me with Glamourer", ref dress)) { Plugin.Config.DressWithGlamourer = dress; Plugin.Config.Save(); }
            ImGui.SameLine();
            var all = Plugin.Config.AllPiecesOn;
            if (ImGui.Checkbox("Turn on every piece", ref all)) { Plugin.Config.AllPiecesOn = all; Plugin.Config.Save(); }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("For mods with options you can tick several of (like a shirt, a mask and pants),\n"
                    + "turn them all on during the photo session. One-choice options (like colors) stay on the mod's default.\n"
                    + "You can change any option on the card while the session is running.");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Glamourer puts on the item each mod replaces, so the mod actually shows.\n"
                    + "Your current look is saved first and put back after each mod and when the session ends.\n"
                    + "Your Glamourer designs, automation and glamour plates are never changed.");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Puts each mod you pick on you, one at a time, so you can screenshot it.\n"
                    + "It only lasts until you move to the next mod or end the session.\n"
                    + "Nothing in your Penumbra collections is changed or saved.\n"
                    + "(Friends synced with you may see each mod while the session runs.)");
        }
        else
        {
            var on = PhotoSession.Wearing == mod.Dir;
            ImGui.TextColored(on ? Gold : Dim, on ? "Wearing this mod now (temporarily)." : PenumbraIpc.LastError.Length > 0 ? PenumbraIpc.LastError : "Putting it on...");
            if (on) DrawOptions(mod);
            if (on && PhotoSession.Dressing)
            {
                if (PhotoSession.Dressed.Count > 0)
                    ImGui.TextColored(Dim, "Glamourer dressed you in: " + string.Join(", ", PhotoSession.Dressed.Select(w => $"{w.Name} ({w.SlotName})")));
                else
                    ImGui.TextColored(Dim, GlamourerIpc.LastError.Length > 0 ? GlamourerIpc.LastError : "Nothing to dress for this one (not a gear mod), so your own look stays on.");
            }
            else if (on && Plugin.Config.DressWithGlamourer && GlamourerIpc.LastError.Length > 0)
            {
                ImGui.PushTextWrapPos(0f);
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.45f, 1f), GlamourerIpc.LastError);
                ImGui.PopTextWrapPos();
            }
        }

        var changed = PenumbraIpc.ChangedItems(mod.Dir);
        if (changed.Count > 0)
        {
            ImGui.TextColored(Dim, "Wear this for it to show:");
            ImGui.SameLine();
            var shown = changed.Take(6).ToList();
            ImGui.TextUnformatted(string.Join(", ", shown) + (changed.Count > shown.Count ? $" (and {changed.Count - shown.Count} more)" : ""));
            if (changed.Count > shown.Count && ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", changed.Take(40)));
        }
        ImGui.Spacing();
    }

    /// <summary>The mod's options during a photo session: tick boxes for pick-several groups, a drop-down for pick-one groups.</summary>
    private static void DrawOptions(ModEntry mod)
    {
        var groups = PenumbraIpc.Options(mod.Dir);
        if (groups.Count == 0) return;
        if (!ImGui.TreeNodeEx("Options for this photo##opts", ImGuiTreeNodeFlags.DefaultOpen)) return;
        foreach (var g in groups)
        {
            ImGui.PushID("grp:" + g.Name);
            PhotoSession.Choices.TryGetValue(g.Name, out var picked);
            if (g.PickSeveral)
            {
                ImGui.TextColored(Dim, g.Name + ":");
                var current = picked ?? new List<string>();
                for (var i = 0; i < g.Options.Length; i++)
                {
                    var opt = g.Options[i];
                    var isOn = current.Contains(opt);
                    if (i > 0) ImGui.SameLine();
                    if (ImGui.Checkbox(opt + "##o" + i, ref isOn))
                    {
                        var next = g.Options.Where(o => o == opt ? isOn : current.Contains(o)).ToList();
                        PhotoSession.SetChoice(g.Name, next);
                    }
                }
                if (picked == null) { ImGui.SameLine(); ImGui.TextColored(Dim, "(mod's default)"); }
            }
            else
            {
                ImGui.TextColored(Dim, g.Name + ":");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200f);
                var shown = picked is { Count: > 0 } ? picked[0] : "(mod's default)";
                if (ImGui.BeginCombo("##one", shown))
                {
                    if (ImGui.Selectable("(mod's default)", picked == null)) PhotoSession.ClearChoice(g.Name);
                    for (var i = 0; i < g.Options.Length; i++)
                        if (ImGui.Selectable(g.Options[i] + "##o" + i, picked is { Count: > 0 } && picked[0] == g.Options[i]))
                            PhotoSession.SetChoice(g.Name, new List<string> { g.Options[i] });
                    ImGui.EndCombo();
                }
            }
            ImGui.PopID();
        }
        ImGui.TreePop();
    }

    private void ShowImageMessage(string dir, string text)
    {
        imageMessageFor = dir;
        imageMessage = text;
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

        ImGui.Spacing();
        ImGui.TextColored(Dim, "Screenshot folder in use: " + Previews.ScreenshotDir());
        ImGui.TextColored(Dim, "Only fill this in if you save screenshots somewhere else:");
        if (shotFolderTyping.Length == 0 && Plugin.Config.ScreenshotFolder.Length > 0) shotFolderTyping = Plugin.Config.ScreenshotFolder;
        ImGui.SetNextItemWidth(Math.Max(200f, ImGui.GetContentRegionAvail().X - 120f));
        ImGui.InputText("##shots", ref shotFolderTyping, 400);
        ImGui.SameLine();
        if (ImGui.Button("Save##shots"))
        {
            Plugin.Config.ScreenshotFolder = shotFolderTyping.Trim();
            Plugin.Config.Save();
        }
    }
}
