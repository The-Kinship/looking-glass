using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LookingGlass;

/// <summary>
/// Talks to Penumbra through its official plugin connection (the same one Glamourer and other plugins use).
/// Only used for: photo session (temporary, locked settings on your own character), "what does this mod change",
/// redrawing you, and opening Penumbra at a mod. Nothing here saves anything in Penumbra.
/// </summary>
public static class PenumbraIpc
{
    /// <summary>Our lock on temporary settings, so ending the session clears only what the Looking Glass set.</summary>
    private const int Key = 0x4C47_4C53;   // "LGLS"
    private const string Source = "Rafter Cat's Looking Glass (photo session)";
    private const int Self = 0;            // your own character
    private const int PriorityOnTop = 999_999;

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultOptions =
        new Dictionary<string, IReadOnlyList<string>>();

    private static string lastError = "";
    /// <summary>The last problem talking to Penumbra ("" if none).</summary>
    public static string LastError => lastError;

    /// <summary>Puts one mod on you temporarily: enabled, above everything else, default options.</summary>
    public static bool TryOn(string modDir)
    {
        try
        {
            var ipc = Plugin.PluginInterface.GetIpcSubscriber<int, string, string,
                ValueTuple<bool, bool, int, IReadOnlyDictionary<string, IReadOnlyList<string>>>, string, int, int>(
                "Penumbra.SetTemporaryModSettingsPlayer.V5");
            var result = ipc.InvokeFunc(Self, modDir, "", (false, true, PriorityOnTop, DefaultOptions), Source, Key);
            if (result != 0) { lastError = "Penumbra said no (code " + result + ")."; return false; }
            Redraw();
            lastError = "";
            return true;
        }
        catch (Exception ex) { return Failed(ex); }
    }

    /// <summary>Takes one mod's temporary setting off again.</summary>
    public static void TakeOff(string modDir)
    {
        try
        {
            Plugin.PluginInterface.GetIpcSubscriber<int, string, string, int, int>("Penumbra.RemoveTemporaryModSettingsPlayer.V5")
                .InvokeFunc(Self, modDir, "", Key);
        }
        catch (Exception ex) { Failed(ex); }
    }

    /// <summary>Clears every temporary setting the Looking Glass made, and redraws you.</summary>
    public static void TakeOffAll()
    {
        try
        {
            Plugin.PluginInterface.GetIpcSubscriber<int, int, int>("Penumbra.RemoveAllTemporaryModSettingsPlayer.V5")
                .InvokeFunc(Self, Key);
            Redraw();
        }
        catch (Exception ex) { Failed(ex); }
    }

    public static void Redraw()
    {
        try { Plugin.PluginInterface.GetIpcSubscriber<int, int, object>("Penumbra.RedrawObject.V5").InvokeAction(Self, 0); }
        catch (Exception ex) { Failed(ex); }
    }

    /// <summary>Opens Penumbra's window on the Mods tab with this mod selected.</summary>
    public static bool Open(string modDir)
    {
        try
        {
            var result = Plugin.PluginInterface.GetIpcSubscriber<int, string, string, int>("Penumbra.OpenMainWindow.V5")
                .InvokeFunc(1, modDir, "");
            lastError = result == 0 ? "" : "Penumbra couldn't open that mod (code " + result + ").";
            return result == 0;
        }
        catch (Exception ex) { return Failed(ex); }
    }

    /// <summary>A wearable item a mod changes: Glamourer's slot number, the item's ID, and its name.</summary>
    public readonly record struct Wearable(byte Slot, ulong ItemId, string Name, string SlotName);

    private static readonly Dictionary<string, List<Wearable>> wearCache = new();

    /// <summary>Glamourer slot numbers for Penumbra's equipment types.</summary>
    private static readonly Dictionary<string, (byte Slot, string Name)> Slots = new()
    {
        ["Head"] = (3, "Head"), ["Body"] = (4, "Body"), ["Hands"] = (5, "Hands"), ["Legs"] = (7, "Legs"),
        ["Feet"] = (8, "Feet"), ["Ears"] = (9, "Earrings"), ["Neck"] = (10, "Necklace"), ["Wrists"] = (11, "Bracelets"),
        ["Finger"] = (12, "Ring"),
    };

    /// <summary>
    /// The gear items a mod changes, one per slot (the first Penumbra lists), for dressing you during a photo session.
    /// Penumbra hands these over as its own item records, so they're read by name and anything unexpected is skipped.
    /// </summary>
    public static List<Wearable> Wearables(string modDir)
    {
        if (wearCache.TryGetValue(modDir, out var cached)) return cached;
        var list = new List<Wearable>();
        try
        {
            var items = Plugin.PluginInterface.GetIpcSubscriber<string, string, Dictionary<string, object?>>("Penumbra.GetChangedItems.V5")
                .InvokeFunc(modDir, "");
            if (items != null)
            {
                foreach (var (name, value) in items.OrderBy(kv => kv.Key))
                {
                    if (value == null) continue;
                    var type = Member(value, "Type")?.ToString() ?? "";
                    if (!Slots.TryGetValue(type, out var slot)) continue;      // weapons, emotes, hair etc. aren't dressed
                    if (list.Any(w => w.Slot == slot.Slot)) continue;          // one item per slot is enough
                    var idObj = Member(value, "Id");
                    var raw = idObj == null ? null : Member(idObj, "Id");
                    if (raw == null) continue;
                    var id = Convert.ToUInt64(raw);
                    if (id == 0) continue;
                    list.Add(new Wearable(slot.Slot, id, name, slot.Name));
                }
            }
        }
        catch (Exception ex) { Failed(ex); }
        wearCache[modDir] = list;
        return list;
    }

    private static object? Member(object o, string name)
    {
        var t = o.GetType();
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f != null) return f.GetValue(o);
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return p?.GetValue(o);
    }

    private static readonly Dictionary<string, List<string>> changedCache = new();

    /// <summary>What the mod changes (item names like "Gambler's Shirt"), as Penumbra lists them. Cached per mod.</summary>
    public static List<string> ChangedItems(string modDir)
    {
        if (changedCache.TryGetValue(modDir, out var cached)) return cached;
        var list = new List<string>();
        try
        {
            var items = Plugin.PluginInterface.GetIpcSubscriber<string, string, Dictionary<string, object?>>("Penumbra.GetChangedItems.V5")
                .InvokeFunc(modDir, "");
            if (items != null) list = items.Keys.Where(k => k.Length > 0).OrderBy(k => k).ToList();
        }
        catch (Exception ex) { Failed(ex); }   // cached as empty; reopening the window tries again
        changedCache[modDir] = list;
        return list;
    }

    public static void ForgetChangedItems()
    {
        changedCache.Clear();
        wearCache.Clear();
    }

    private static bool Failed(Exception ex)
    {
        Plugin.Log.Warning(ex, "Penumbra call failed.");
        lastError = "Penumbra isn't answering. Is it installed and turned on?";
        return false;
    }
}
