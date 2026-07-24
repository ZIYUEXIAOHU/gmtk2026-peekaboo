using UnityEngine;

/// <summary>
/// [已废弃] 旧版本地对局状态机：私有 GameState 枚举（Waiting/Hiding/Seeking/Ended）、
/// 写死的 30/60 秒时长、直接写 UI Text。
///
/// 对局权威状态机已迁移至契约实现 <see cref="NetworkGameState"/>
/// （Assets/Scripts/Network/NetworkGameState.cs），覆盖 GamePhase（Waiting/Prep/Playing/Ended）、
/// 身份名额、房主开局，数值全部来自 <see cref="GameConstants"/>。
/// UI 表现由程序 2 经 <see cref="GameContract"/> 读取，不再由本类直接驱动。
///
/// 本类仅保留空壳（不再是 NetworkBehaviour，不再持有任何状态），
/// 避免旧场景/脚本对 GameManager.StartGame() 的残留引用编译失败。
/// 可在完成场景清理后安全删除本文件与其挂载物体。
/// </summary>
public class GameManager : MonoBehaviour
{
    public void StartGame()
    {
        Debug.LogWarning("[GameManager] 已废弃，对局请改用 GameContract.Commands.HostStartGame()（见 NetworkGameState）。");
    }
}
