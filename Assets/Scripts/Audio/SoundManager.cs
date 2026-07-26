using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 音效管理器 - 实现 IAudioService 契约
/// 程序1 实现，程序2 通过 GameContract.Audio 调用
/// </summary>
public class SoundManager : MonoBehaviour, IAudioService
{
    public static SoundManager Instance { get; private set; }

    [Header("Audio Mixer")]
    public AudioMixer audioMixer;

    [Header("音效源")]
    public AudioSource musicSource;              // 背景音乐
    public AudioSource sfxSource;                // 普通音效
    public AudioSource globalSource;             // 3D 音效

    [Header("抓捕者双音效源（心跳 + 铅笔摩擦）")]
    public AudioSource seekerHeartbeatSource;    // 心跳音效专用
    public AudioSource seekerPencilSource;       // 铅笔摩擦音效专用

    [Header("UI 音效")]
    public AudioClip clickSound;
    public AudioClip hoverSound;

    [Header("全玩家音效")]
    public AudioClip slashSound;
    public AudioClip hitSound;
    public AudioClip searchSound;
    public AudioClip popSound;
    public AudioClip cuckooSound;
    public AudioClip countdownSound;
    public AudioClip footstepSound;

    [Header("抓捕者音效（距离相关）")]
    public AudioClip heartbeatSound;             // 心跳音效
    public AudioClip pencilSound;                // 铅笔在纸上摩擦

    [Header("本地音效（仅躲藏者）")]
    public AudioClip transformSound;
    public AudioClip placeSound;

    [Header("背景音乐")]
    [Tooltip("全局背景音乐（主菜单/大厅一直播放）")]
    public AudioClip globalMusic;
    [Tooltip("游戏背景音乐（进入对局时切换）")]
    public AudioClip gameMusic;

    private float currentMasterVolume = GameConstants.DefaultMasterVolume;
    private float currentMusicVolume = GameConstants.DefaultMusicVolume;
    private float currentSFXVolume = GameConstants.DefaultSFXVolume;

    // 当前播放状态
    private bool isHeartbeatPlaying = false;
    private bool isPencilPlaying = false;

    void Awake()
    {
        // ===== 单例保护：防止重复创建 =====
        if (Instance != null)
        {
            Debug.LogWarning("⚠️ 重复的 SoundManager，已销毁");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // ===== 绑定到契约 =====
        GameContract.BindAudio(this);

        // 加载保存的音量设置
        LoadVolumes();
        ApplyVolumes();

        // 设为不销毁（场景切换时保留）
        DontDestroyOnLoad(gameObject);

        Debug.Log("✅ SoundManager 已初始化并绑定到契约");
    }

    void Start()
    {
        // ===== 播放全局背景音乐 =====
        if (globalMusic != null)
        {
            PlayMusic(globalMusic, currentMusicVolume);
        }
    }

    void OnDestroy()
    {
        GameContract.UnbindAudio();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ==================== 音量加载与保存（玩家档案） ====================

    void LoadVolumes()
    {
        PlayerProfile.Load();
        currentMasterVolume = PlayerProfile.MasterVolume;
        currentMusicVolume = PlayerProfile.MusicVolume;
        currentSFXVolume = PlayerProfile.SFXVolume;
    }

    void ApplyVolumes()
    {
        ApplyMasterVolume(currentMasterVolume);
        ApplyMusicVolume(currentMusicVolume);
        ApplySFXVolume(currentSFXVolume);
    }

    // ==================== IAudioService 实现 - 音量 ====================

    public void SetMasterVolume(float value)
    {
        ApplyMasterVolume(value);
        PlayerProfile.SetMasterVolume(currentMasterVolume);
    }

    public void SetMusicVolume(float value)
    {
        ApplyMusicVolume(value);
        PlayerProfile.SetMusicVolume(currentMusicVolume);
    }

    public void SetSFXVolume(float value)
    {
        ApplySFXVolume(value);
        PlayerProfile.SetSFXVolume(currentSFXVolume);
    }

    void ApplyMasterVolume(float value)
    {
        currentMasterVolume = Mathf.Clamp01(value);
        if (audioMixer != null)
        {
            audioMixer.SetFloat("MasterVolume", Mathf.Log10(Mathf.Max(0.0001f, currentMasterVolume)) * 20);
        }
        else
        {
            AudioListener.volume = currentMasterVolume;
        }
    }

    void ApplyMusicVolume(float value)
    {
        currentMusicVolume = Mathf.Clamp01(value);

        if (musicSource != null)
        {
            musicSource.volume = currentMusicVolume;
            Debug.Log($"🎵 音乐音量设置为: {currentMusicVolume}");
        }
        else
        {
            Debug.LogWarning("⚠️ musicSource 未绑定，无法设置音乐音量");
        }
    }

    void ApplySFXVolume(float value)
    {
        currentSFXVolume = Mathf.Clamp01(value);
        if (audioMixer != null)
        {
            audioMixer.SetFloat("SFXVolume", Mathf.Log10(Mathf.Max(0.0001f, currentSFXVolume)) * 20);
        }
        else
        {
            if (sfxSource != null) sfxSource.volume = currentSFXVolume;
            if (globalSource != null) globalSource.volume = currentSFXVolume;
            if (seekerHeartbeatSource != null) seekerHeartbeatSource.volume = currentSFXVolume;
            if (seekerPencilSource != null) seekerPencilSource.volume = currentSFXVolume;
        }
    }

    public float GetMasterVolume() => currentMasterVolume;
    public float GetMusicVolume() => currentMusicVolume;
    public float GetSFXVolume() => currentSFXVolume;

    // ==================== IAudioService 实现 - 音效播放 ====================

    // ---- UI ----
    public void PlayClick()
    {
        PlaySFX(clickSound, 0.5f);
    }

    public void PlayHover()
    {
        PlaySFX(hoverSound, 0.3f);
    }

    // ---- 全玩家（3D） ----
    public void PlaySlash(Vector3 position)
    {
        PlayGlobal(slashSound, position, 0.7f);
    }

    public void PlayHit(Vector3 position)
    {
        PlayGlobal(hitSound, position, 0.8f);
    }

    public void PlaySearch(Vector3 position)
    {
        PlayGlobal(searchSound, position, 0.6f);
    }

    public void PlayPop(Vector3 position)
    {
        PlayGlobal(popSound, position, 0.9f);
    }

    public void PlayCuckoo(Vector3 position)
    {
        PlayGlobal(cuckooSound, position, 0.7f);
    }

    public void PlayCountdown()
    {
        PlaySFX(countdownSound, 0.6f);
    }

    public void PlayFootstep(Vector3 position)
    {
        PlayGlobal(footstepSound, position, 0.4f);
    }

    // ---- 心跳（兼容 IAudioService 接口） ----
    public void PlayHeartbeat()
    {
        // 使用默认参数播放心跳（兼容旧接口调用）
        if (heartbeatSound == null || seekerHeartbeatSource == null) return;
        
        seekerHeartbeatSource.clip = heartbeatSound;
        seekerHeartbeatSource.loop = true;
        seekerHeartbeatSource.volume = 0.3f * currentSFXVolume;
        
        if (!seekerHeartbeatSource.isPlaying)
        {
            seekerHeartbeatSource.Play();
            isHeartbeatPlaying = true;
        }
    }

    // ---- 本地（仅躲藏者） ----
    public void PlayTransformLocal()
    {
        PlaySFX(transformSound, 0.6f);
    }

    public void PlayPlaceLocal()
    {
        PlaySFX(placeSound, 0.7f);
    }

    // ==================== 抓捕者双音效（心跳 + 铅笔摩擦） ====================

    /// <summary>
    /// 更新抓捕者音效（心跳 + 铅笔摩擦），距离越近音量越大
    /// </summary>
    public void UpdateSeekerSounds(float distance, float maxDistance = 20f)
    {
        // 距离归一化（0 = 最近，1 = 最远）
        float normalized = Mathf.Clamp01(distance / maxDistance);
        // 音量：距离越近越大，范围 0.05 ~ 0.5
        float volume = Mathf.Lerp(0.5f, 0.05f, normalized);

        UpdateHeartbeat(volume);
        UpdatePencil(volume);
    }

    private void UpdateHeartbeat(float volume)
    {
        if (seekerHeartbeatSource == null || heartbeatSound == null) return;

        seekerHeartbeatSource.clip = heartbeatSound;
        seekerHeartbeatSource.loop = true;
        seekerHeartbeatSource.volume = volume * currentSFXVolume;

        if (!seekerHeartbeatSource.isPlaying)
        {
            seekerHeartbeatSource.Play();
            isHeartbeatPlaying = true;
        }
    }

    private void UpdatePencil(float volume)
    {
        if (seekerPencilSource == null || pencilSound == null) return;

        seekerPencilSource.clip = pencilSound;
        seekerPencilSource.loop = true;
        seekerPencilSource.volume = volume * currentSFXVolume * 0.7f; // 铅笔音效稍轻

        if (!seekerPencilSource.isPlaying)
        {
            seekerPencilSource.Play();
            isPencilPlaying = true;
        }
    }

    public void StopSeekerSounds()
    {
        if (seekerHeartbeatSource != null && seekerHeartbeatSource.isPlaying)
        {
            seekerHeartbeatSource.Stop();
            isHeartbeatPlaying = false;
        }

        if (seekerPencilSource != null && seekerPencilSource.isPlaying)
        {
            seekerPencilSource.Stop();
            isPencilPlaying = false;
        }
    }

    public bool IsHeartbeatPlaying() => isHeartbeatPlaying;
    public bool IsPencilPlaying() => isPencilPlaying;

    // ==================== IAudioService 实现 - 背景音乐 ====================

    public void PlayMusic(AudioClip music, float volume = 0.3f)
    {
        if (music == null || musicSource == null) return;
        musicSource.clip = music;
        musicSource.loop = true;
        // 使用传入的 volume，如果传入 -1 则使用 currentMusicVolume
        musicSource.volume = volume >= 0 ? volume : currentMusicVolume;
        musicSource.Play();
        Debug.Log($"🎵 播放音乐：{music.name}，音量：{musicSource.volume}");
    }

    public void StopMusic()
    {
        if (musicSource != null) musicSource.Stop();
        Debug.Log("🎵 停止音乐");
    }

    public void PauseMusic()
    {
        if (musicSource != null) musicSource.Pause();
        Debug.Log("🎵 暂停音乐");
    }

    public void ResumeMusic()
    {
        if (musicSource != null) musicSource.UnPause();
        Debug.Log("🎵 恢复音乐");
    }

    // ==================== 内部播放方法 ====================

    private void PlaySFX(AudioClip clip, float volume = 1f)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, volume * currentSFXVolume);
    }

    private void PlayGlobal(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null || globalSource == null) return;
        globalSource.transform.position = position;
        globalSource.PlayOneShot(clip, volume * currentSFXVolume);
    }

    // ==================== 便捷方法 ====================

    /// <summary>
    /// 切换背景音乐
    /// </summary>
    public void SwitchToMusic(AudioClip newMusic)
    {
        if (newMusic != null)
        {
            PlayMusic(newMusic, currentMusicVolume);
        }
    }

    /// <summary>
    /// 切换到游戏背景音乐
    /// </summary>
    public void SwitchToGameMusic()
    {
        if (gameMusic != null)
        {
            PlayMusic(gameMusic, currentMusicVolume);
        }
    }

    /// <summary>
    /// 切换到全局/菜单背景音乐
    /// </summary>
    public void SwitchToMenuMusic()
    {
        if (globalMusic != null)
        {
            PlayMusic(globalMusic, currentMusicVolume);
        }
    }
}