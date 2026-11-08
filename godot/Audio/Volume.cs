// The volume setting: one slider (the menu's main page), 0 to 1, kept in user://settings.cfg.
// A setting of the player's machine, not of the game: never in the sim's state or a save.

using Godot;

namespace LoonieRoo.Game.Audio;

public static class Volume
{
    const string FILE = "user://settings.cfg";
    public const float DEFAULT = 0.8f;
    static float? level;

    /// <summary>The volume, 0 (silent) to 1, read from the settings file the first time.</summary>
    public static float Level
    {
        get
        {
            if (level == null)
            {
                var cfg = new ConfigFile();
                level = cfg.Load(FILE) == Error.Ok ? Mathf.Clamp((float)cfg.GetValue("audio", "volume", DEFAULT), 0, 1) : DEFAULT;
                Apply();
            }
            return level.Value;
        }
    }

    public static void Set(float v)
    {
        level = Mathf.Clamp(v, 0, 1);
        Apply();
        var cfg = new ConfigFile();
        cfg.Load(FILE); // keep whatever else is in there
        cfg.SetValue("audio", "volume", level.Value);
        cfg.Save(FILE);
    }

    /// <summary>The master bus: the slider squared (closer to how loudness is heard), muted at 0.</summary>
    static void Apply()
    {
        float v = level ?? DEFAULT;
        AudioServer.SetBusMute(0, v <= 0.001f);
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Max(v * v, 0.0001f)));
    }
}
