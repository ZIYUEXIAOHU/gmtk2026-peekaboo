using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Mirror;
using System.Collections.Generic;

public class SeekerMouseController : NetworkBehaviour
{
    [Header("鼠标贴图")]
    public Texture2D defaultCursor;
    public Texture2D attackCursor;
    public Vector2 hotSpot = Vector2.zero;
    
    [Header("悬停检测")]
    public LayerMask targetLayer;
    
    private bool isLocalPlayerReady = false;
    private HashSet<uint> hiderNetIds = new HashSet<uint>();  // 缓存存活躲藏者 NetId
    private SeekerController seekerController;
    static readonly List<RaycastResult> uiRaycastBuffer = new List<RaycastResult>(8);
    
    void Start()
    {
        if (!isLocalPlayer) return;
        
        isLocalPlayerReady = true;
        CacheSeekerController();
        
        if (defaultCursor != null)
        {
            Cursor.SetCursor(defaultCursor, hotSpot, CursorMode.Auto);
        }
    }
    
    void Update()
    {
        if (!isLocalPlayerReady || !isLocalPlayer) return;
        
        // ===== 更新存活躲藏者列表（通过契约） =====
        UpdateHiderList();
        
        // ===== 检测鼠标悬停目标 =====
        DetectHoverTarget();
        
        // ===== 鼠标左键攻击（仅点在可交互按钮等控件上时跳过）=====
        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            if (IsPointerOverInteractableUI())
                return;

            CacheSeekerController();
            seekerController?.TryPerformAttack();
        }
    }

    void CacheSeekerController()
    {
        if (seekerController != null) return;
        // 组件挂在 Visual_Seeker 上，控制器在根节点
        seekerController = GetComponentInParent<SeekerController>();
        if (seekerController == null)
            seekerController = GetComponent<SeekerController>();
    }

    /// <summary>
    /// 仅当指针下有可交互 UI（Button / Selectable）时视为点按钮。
    /// 不用 IsPointerOverGameObject：全屏透明 Image（LobbyUI 等）会误挡全部攻击。
    /// </summary>
    static bool IsPointerOverInteractableUI()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        var eventData = new PointerEventData(es) { position = Input.mousePosition };
        uiRaycastBuffer.Clear();
        es.RaycastAll(eventData, uiRaycastBuffer);

        for (int i = 0; i < uiRaycastBuffer.Count; i++)
        {
            GameObject go = uiRaycastBuffer[i].gameObject;
            if (go == null) continue;

            var selectable = go.GetComponentInParent<Selectable>();
            if (selectable != null && selectable.IsActive() && selectable.IsInteractable())
                return true;
        }

        return false;
    }
    
    /// <summary>
    /// 通过契约获取所有存活躲藏者的 NetId
    /// </summary>
    void UpdateHiderList()
    {
        hiderNetIds.Clear();
        
        if (!GameContract.IsBound || GameContract.State == null) return;
        
        foreach (var player in GameContract.State.Players)
        {
            if (player != null && 
                player.Role == PlayerRole.Hider && 
                player.HiderState != HiderState.Captured)
            {
                hiderNetIds.Add(player.NetId);
            }
        }
    }
    
    void DetectHoverTarget()
    {
        if (Camera.main == null) return;

        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 mousePosition = new Vector2(mousePos.x, mousePos.y);
        
        RaycastHit2D hit = Physics2D.Raycast(mousePosition, Vector2.zero, 0f, targetLayer);
        
        bool isTargetHider = false;
        
        if (hit.collider != null)
        {
            // ===== 通过契约检查是否是躲藏者 =====
            RoomPlayer rp = hit.collider.GetComponent<RoomPlayer>();
            if (rp != null && hiderNetIds.Contains(rp.netId))
            {
                isTargetHider = true;
            }
        }
        
        // ===== 切换鼠标 =====
        if (isTargetHider && attackCursor != null)
        {
            Cursor.SetCursor(attackCursor, hotSpot, CursorMode.Auto);
        }
        else if (defaultCursor != null)
        {
            Cursor.SetCursor(defaultCursor, hotSpot, CursorMode.Auto);
        }
    }
}
