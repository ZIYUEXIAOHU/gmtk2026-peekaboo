using UnityEngine;

/// <summary>
/// 音效服务契约 - 通过 GameContract.Audio 访问
/// 程序1 实现此接口，程序2 通过契约调用
/// </summary>
public interface IAudioService
{
    // ===== UI 音效 =====
    void PlayClick();
    void PlayHover();

    // ===== 全玩家音效（3D 位置音效） =====
    void PlaySlash(Vector3 position);
    void PlayHit(Vector3 position);
    void PlaySearch(Vector3 position);
    void PlayPop(Vector3 position);
    void PlayCuckoo(Vector3 position);
    void PlayCountdown();
    void PlayFootstep(Vector3 position);
    void PlayHeartbeat();

    // ===== 本地音效（仅躲藏者） =====
    void PlayTransformLocal();
    void PlayPlaceLocal();

    // ===== 背景音乐 =====
    void PlayMusic(AudioClip music, float volume = 0.3f);
    void StopMusic();
    void PauseMusic();
    void ResumeMusic();

    // ===== 音量控制 =====
    void SetMasterVolume(float value);
    void SetMusicVolume(float value);
    void SetSFXVolume(float value);
    float GetMasterVolume();
    float GetMusicVolume();
    float GetSFXVolume();
}