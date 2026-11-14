using System;
using System.IO;
using System.Linq;
using Dalamud.Interface.Textures.TextureWraps;

namespace LookingGlass;

/// <summary>
/// Preview pictures: each one is a copy kept in the plugin's own folder (pluginConfigs/LookingGlass/images),
/// so deleting or moving the original screenshot doesn't lose it.
/// </summary>
public static class Previews
{
    private static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".bmp" };

    public static string ImagesDir
    {
        get
        {
            var dir = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "images");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>The game's screenshot folder (or the one you set in Settings).</summary>
    public static string ScreenshotDir()
    {
        var manual = Plugin.Config.ScreenshotFolder.Trim();
        if (manual.Length > 0) return manual;
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "My Games", "FINAL FANTASY XIV - A Realm Reborn", "screenshots");
    }

    /// <summary>The newest picture in the screenshot folder, or null.</summary>
    public static FileInfo? LatestScreenshot()
    {
        try
        {
            var dir = new DirectoryInfo(ScreenshotDir());
            if (!dir.Exists) return null;
            return dir.EnumerateFiles()
                .Where(f => ImageTypes.Contains(f.Extension.ToLowerInvariant()))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't look in the screenshot folder.");
            return null;
        }
    }

    public static bool IsImage(string path) => ImageTypes.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Copies a picture in as this mod's preview (replacing any old one) and resets the framing.</summary>
    public static string? Attach(string modDir, string source)
    {
        try
        {
            if (!File.Exists(source) || !IsImage(source)) return "That isn't a picture file (png, jpg or bmp).";
            var safe = string.Concat(modDir.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            if (safe.Length > 60) safe = safe[..60];
            // A new name each time, so the new picture shows straight away instead of the old one.
            var name = $"{safe}-{DateTime.UtcNow.Ticks}{Path.GetExtension(source).ToLowerInvariant()}";
            File.Copy(source, Path.Combine(ImagesDir, name));

            var info = Plugin.Config.EditInfo(modDir);
            DeleteFile(info.ImageFile);
            info.ImageFile = name;
            info.Zoom = 1f;
            info.CenterX = 0.5f;
            info.CenterY = 0.5f;
            Plugin.Config.Save();
            return null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Couldn't add the preview.");
            return "Couldn't add the picture: " + ex.Message;
        }
    }

    public static void Remove(string modDir)
    {
        var info = Plugin.Config.InfoFor(modDir);
        if (info == null || info.ImageFile.Length == 0) return;
        DeleteFile(info.ImageFile);
        info.ImageFile = "";
        Plugin.Config.Save();
    }

    private static void DeleteFile(string name)
    {
        if (name.Length == 0) return;
        try { File.Delete(Path.Combine(ImagesDir, name)); } catch (Exception) { /* not there any more */ }
    }

    /// <summary>The loaded picture for a mod (null while it loads, or if it has none).</summary>
    public static IDalamudTextureWrap? Get(ModInfo? info)
    {
        if (info == null || info.ImageFile.Length == 0) return null;
        var path = Path.Combine(ImagesDir, info.ImageFile);
        if (!File.Exists(path)) return null;
        try { return Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault(); }
        catch (Exception) { return null; }
    }
}
