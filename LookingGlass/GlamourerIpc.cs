using System;
using System.Collections.Generic;

namespace LookingGlass;

/// <summary>
/// Talks to Glamourer through its official plugin connection, only during a photo session:
/// snapshot your current look, put single items on you, and put the snapshot back.
/// Your saved designs, automation and glamour plates are never touched.
/// </summary>
public static class GlamourerIpc
{
    private const uint Key = 0x4C47_4C53;   // "LGLS"
    private const int Self = 0;
    private const ulong Once = 0x01;          // apply once, don't force anything into automation
    private const ulong EquipmentAndLooks = 0x01 | 0x02 | 0x04;   // once + equipment + customization (your whole look)

    // A List (not a byte[]): Dalamud passes byte arrays between plugins as text, which Glamourer can't read back as dyes.
    private static readonly IReadOnlyList<byte> NoDye = new List<byte> { 0, 0 };

    /// <summary>Your look from just before the session ("" = none taken).</summary>
    private static string snapshot = "";

    private static string lastError = "";
    public static string LastError => lastError;
    public static bool HasSnapshot => snapshot.Length > 0;

    /// <summary>Remembers your current look. Returns false (and the session won't dress you) if Glamourer can't give it.</summary>
    public static bool TakeSnapshot()
    {
        snapshot = "";
        try
        {
            var (ec, data) = Plugin.PluginInterface.GetIpcSubscriber<int, uint, ValueTuple<int, string?>>("Glamourer.GetStateBase64")
                .InvokeFunc(Self, Key);
            if (ec != 0 || string.IsNullOrEmpty(data)) { lastError = "Glamourer couldn't save your current look (code " + ec + "), so it won't dress you."; return false; }
            snapshot = data;
            lastError = "";
            return true;
        }
        catch (Exception ex) { return Failed(ex, "saving your look"); }
    }

    /// <summary>Puts your look back to the snapshot (without forgetting it, so it can be used again).</summary>
    public static void Restore()
    {
        if (snapshot.Length == 0) return;
        try
        {
            Plugin.PluginInterface.GetIpcSubscriber<object, int, uint, ulong, int>("Glamourer.ApplyState")
                .InvokeFunc(snapshot, Self, Key, EquipmentAndLooks);
        }
        catch (Exception ex) { Failed(ex, "putting your look back"); }
    }

    /// <summary>Puts your look back, unlocks anything we touched, and forgets the snapshot.</summary>
    public static void RestoreAndForget()
    {
        Restore();
        try { Plugin.PluginInterface.GetIpcSubscriber<int, uint, int>("Glamourer.UnlockState").InvokeFunc(Self, Key); }
        catch (Exception) { /* nothing was locked */ }
        snapshot = "";
    }

    /// <summary>Puts one item on you in one slot (undyed).</summary>
    public static bool Wear(byte slot, ulong itemId)
    {
        try
        {
            var ec = Plugin.PluginInterface.GetIpcSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int>("Glamourer.SetItem.V3")
                .InvokeFunc(Self, slot, itemId, NoDye, Key, Once);
            lastError = ec == 0 ? "" : "Glamourer couldn't put that item on (code " + ec + ").";
            return ec == 0;
        }
        catch (Exception ex) { return Failed(ex, "putting the item on"); }
    }

    private static bool Failed(Exception ex, string step)
    {
        Plugin.Log.Warning(ex, "Glamourer call failed while " + step + ".");
        // Show the real reason, so problems can be tracked down from a screenshot.
        var inner = ex.InnerException ?? ex;
        lastError = $"Glamourer problem while {step}: {inner.GetType().Name}: {inner.Message}";
        return false;
    }
}
