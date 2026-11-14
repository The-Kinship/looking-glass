using System;
using System.Collections.Generic;
using System.Linq;

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

    public static void ForgetChangedItems() => changedCache.Clear();

    private static bool Failed(Exception ex)
    {
        Plugin.Log.Warning(ex, "Penumbra call failed.");
        lastError = "Penumbra isn't answering. Is it installed and turned on?";
        return false;
    }
}
