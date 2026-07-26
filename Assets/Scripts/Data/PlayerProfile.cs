using UnityEngine;

/// <summary>
/// 本地玩家档案：展示名与音量等本机偏好。
/// 启动时自动从 PlayerPrefs 读取，修改时写回并 Save。
/// </summary>
public static class PlayerProfile
{
    public static string PlayerName { get; private set; } = GameConstants.DefaultPlayerName;
    public static float MasterVolume { get; private set; } = GameConstants.DefaultMasterVolume;
    public static float MusicVolume { get; private set; } = GameConstants.DefaultMusicVolume;
    public static float SFXVolume { get; private set; } = GameConstants.DefaultSFXVolume;

    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InitOnStartup()
    {
        Load();
    }

    /// <summary>从 PlayerPrefs 读取档案（启动时自动调用；也可手动刷新）。</summary>
    public static void Load()
    {
        string rawName = PlayerPrefs.GetString(GameConstants.PlayerNamePrefsKey, string.Empty);
        PlayerName = string.IsNullOrWhiteSpace(rawName)
            ? GameConstants.DefaultPlayerName
            : RoomPlayer.SanitizePlayerName(rawName);

        MasterVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(GameConstants.MasterVolumePrefsKey, GameConstants.DefaultMasterVolume));
        MusicVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(GameConstants.MusicVolumePrefsKey, GameConstants.DefaultMusicVolume));
        SFXVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(GameConstants.SFXVolumePrefsKey, GameConstants.DefaultSFXVolume));

        loaded = true;
        Debug.Log(
            $"📂 玩家档案已加载: name={PlayerName}, master={MasterVolume:F2}, music={MusicVolume:F2}, sfx={SFXVolume:F2}");
    }

    public static void SetPlayerName(string name, bool save = true)
    {
        EnsureLoaded();
        PlayerName = RoomPlayer.SanitizePlayerName(name);
        PlayerPrefs.SetString(GameConstants.PlayerNamePrefsKey, PlayerName);
        if (save) PlayerPrefs.Save();
    }

    public static void SetMasterVolume(float value, bool save = true)
    {
        EnsureLoaded();
        MasterVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(GameConstants.MasterVolumePrefsKey, MasterVolume);
        if (save) PlayerPrefs.Save();
    }

    public static void SetMusicVolume(float value, bool save = true)
    {
        EnsureLoaded();
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(GameConstants.MusicVolumePrefsKey, MusicVolume);
        if (save) PlayerPrefs.Save();
    }

    public static void SetSFXVolume(float value, bool save = true)
    {
        EnsureLoaded();
        SFXVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(GameConstants.SFXVolumePrefsKey, SFXVolume);
        if (save) PlayerPrefs.Save();
    }

    /// <summary>一次性写入三项音量并 Save（减少滑块拖动时的磁盘写入）。</summary>
    public static void SetVolumes(float master, float music, float sfx, bool save = true)
    {
        EnsureLoaded();
        MasterVolume = Mathf.Clamp01(master);
        MusicVolume = Mathf.Clamp01(music);
        SFXVolume = Mathf.Clamp01(sfx);
        PlayerPrefs.SetFloat(GameConstants.MasterVolumePrefsKey, MasterVolume);
        PlayerPrefs.SetFloat(GameConstants.MusicVolumePrefsKey, MusicVolume);
        PlayerPrefs.SetFloat(GameConstants.SFXVolumePrefsKey, SFXVolume);
        if (save) PlayerPrefs.Save();
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }

    static void EnsureLoaded()
    {
        if (!loaded) Load();
    }
}
