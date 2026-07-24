// ============================================================
// 契约文件：绑定入口
// 程序 1（NetworkXxx）或程序 2 的 Mock（MockXxx）在启动时调用 Bind 注入实现，
// 表现/UI 代码一律通过 GameContract.* 访问，不直接引用具体实现类。
// ============================================================

using System;
using UnityEngine;

public static class GameContract
{
    // ---- 对局 ----
    public static IGameStateReadonly State { get; private set; }
    public static IGameCommands Commands { get; private set; }
    public static IGameEvents Events { get; private set; }

    // ---- 房间 ----
    public static IRoomStateReadonly RoomState { get; private set; }
    public static IRoomCommands RoomCommands { get; private set; }
    public static IRoomEvents RoomEvents { get; private set; }

    public static bool IsBound => State != null && Commands != null && Events != null;
    public static bool IsRoomBound => RoomState != null && RoomCommands != null && RoomEvents != null;

    public static void Bind(IGameStateReadonly state, IGameCommands commands, IGameEvents events)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (commands == null) throw new ArgumentNullException(nameof(commands));
        if (events == null) throw new ArgumentNullException(nameof(events));

        State = state;
        Commands = commands;
        Events = events;
        Debug.Log($"[GameContract] 绑定对局实现：{state.GetType().Name}");
    }

    public static void BindRoom(IRoomStateReadonly roomState, IRoomCommands roomCommands, IRoomEvents roomEvents)
    {
        if (roomState == null) throw new ArgumentNullException(nameof(roomState));
        if (roomCommands == null) throw new ArgumentNullException(nameof(roomCommands));
        if (roomEvents == null) throw new ArgumentNullException(nameof(roomEvents));

        RoomState = roomState;
        RoomCommands = roomCommands;
        RoomEvents = roomEvents;
        Debug.Log($"[GameContract] 绑定房间实现：{roomState.GetType().Name}");
    }

    public static void Unbind()
    {
        State = null;
        Commands = null;
        Events = null;
    }

    public static void UnbindRoom()
    {
        RoomState = null;
        RoomCommands = null;
        RoomEvents = null;
    }
}
