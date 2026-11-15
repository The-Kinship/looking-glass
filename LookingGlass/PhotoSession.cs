using System.Collections.Generic;
using System.Linq;

namespace LookingGlass;

/// <summary>
/// Photo session: while it's on, whichever mod you pick is put on you temporarily (one at a time),
/// and ending it puts everything back exactly as it was.
/// </summary>
public static class PhotoSession
{
    public static bool Active { get; private set; }
    /// <summary>The mod currently put on you ("" = none).</summary>
    public static string Wearing { get; private set; } = "";
    /// <summary>Items Glamourer put on you for the current mod (empty if none).</summary>
    public static List<PenumbraIpc.Wearable> Dressed { get; private set; } = new();
    /// <summary>True when Glamourer dressing is on for this session (a snapshot of your look was taken).</summary>
    public static bool Dressing { get; private set; }

    /// <summary>The last mod we tried to put on, so a failed one isn't retried every frame.</summary>
    private static string tried = "";

    public static void Start()
    {
        Active = true;
        Wearing = "";
        tried = "";
        Dressed = new();
        // Only dress you if your current look could be saved first, so it can always be put back.
        Dressing = Plugin.Config.DressWithGlamourer && GlamourerIpc.TakeSnapshot();
    }

    /// <summary>Called as you move through the list: takes the last mod off and puts this one on.</summary>
    public static void Show(string modDir)
    {
        if (!Active || modDir == tried) return;
        tried = modDir;
        if (Wearing.Length > 0) PenumbraIpc.TakeOff(Wearing);

        // Back to your own look first, so pieces from the last mod never pile up.
        if (Dressing && Dressed.Count > 0) GlamourerIpc.Restore();
        Dressed = new();

        Wearing = PenumbraIpc.TryOn(modDir) ? modDir : "";
        if (Wearing.Length == 0) PenumbraIpc.Redraw();

        if (Dressing && Wearing.Length > 0)
            foreach (var w in PenumbraIpc.Wearables(modDir))
                if (GlamourerIpc.Wear(w.Slot, w.ItemId)) Dressed.Add(w);
    }

    public static void End()
    {
        if (!Active) return;
        Active = false;
        Wearing = "";
        tried = "";
        PenumbraIpc.TakeOffAll();
        if (Dressing) GlamourerIpc.RestoreAndForget();
        Dressing = false;
        Dressed = new();
    }
}
