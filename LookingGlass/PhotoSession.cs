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
    /// <summary>The last mod we tried to put on, so a failed one isn't retried every frame.</summary>
    private static string tried = "";

    public static void Start()
    {
        Active = true;
        Wearing = "";
        tried = "";
    }

    /// <summary>Called as you move through the list: takes the last mod off and puts this one on.</summary>
    public static void Show(string modDir)
    {
        if (!Active || modDir == tried) return;
        tried = modDir;
        if (Wearing.Length > 0) PenumbraIpc.TakeOff(Wearing);
        Wearing = PenumbraIpc.TryOn(modDir) ? modDir : "";
        if (Wearing.Length == 0) PenumbraIpc.Redraw();
    }

    public static void End()
    {
        if (!Active) return;
        Active = false;
        Wearing = "";
        tried = "";
        PenumbraIpc.TakeOffAll();
    }
}
