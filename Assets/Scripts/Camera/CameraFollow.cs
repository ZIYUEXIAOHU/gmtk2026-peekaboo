using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("跟随设置")]
    public float smoothSpeed = 0.125f;
    public Vector3 offset = new Vector3(0, 0, -10);
    
    [Header("边界限制")]
    public bool useBounds = false;
    public Vector2 minBounds;
    public Vector2 maxBounds;
    
    private Transform target;
    private Vector3 velocity = Vector3.zero;
    private bool isFollowing = false;
    private int lastCheckFrame = -1;
    private const int CheckInterval = 30;
    private uint lastLocalPlayerNetId = 0;
    
    void Start()
    {
        FindLocalPlayer();
    }
    
    void Update()
    {
        if (Time.frameCount - lastCheckFrame > CheckInterval)
        {
            FindLocalPlayer();
            lastCheckFrame = Time.frameCount;
        }
        
        if (target == null)
        {
            FindLocalPlayer();
        }
    }
    
    void LateUpdate()
    {
        if (!isFollowing || target == null) return;
        
        Vector3 desiredPosition = target.position + offset;
        Vector3 smoothedPosition = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref velocity,
            smoothSpeed
        );
        
        if (useBounds)
        {
            smoothedPosition.x = Mathf.Clamp(smoothedPosition.x, minBounds.x, maxBounds.x);
            smoothedPosition.y = Mathf.Clamp(smoothedPosition.y, minBounds.y, maxBounds.y);
        }
        
        transform.position = smoothedPosition;
    }
    
    void FindLocalPlayer()
    {
        // ===== 通过契约获取本地玩家状态 =====
        if (!GameContract.IsBound)
        {
            Debug.LogWarning("⚠️ GameContract 未绑定，无法获取本地玩家");
            return;
        }
        
        IPlayerStateReadonly localPlayer = GameContract.State?.LocalPlayer;
        if (localPlayer == null)
        {
            return;
        }
        
        // ===== 如果 NetId 没变，不需要重新查找 =====
        if (lastLocalPlayerNetId == localPlayer.NetId && target != null)
        {
            return;
        }
        
        lastLocalPlayerNetId = localPlayer.NetId;
        
        // ===== 查找对应的 RoomPlayer =====
        RoomPlayer[] roomPlayers = FindObjectsOfType<RoomPlayer>();
        foreach (var rp in roomPlayers)
        {
            if (rp != null && rp.netId == localPlayer.NetId)
            {
                target = rp.transform;
                isFollowing = true;
                Debug.Log($"✅ 相机跟随本地玩家: {localPlayer.PlayerName} (NetId: {localPlayer.NetId})");
                return;
            }
        }
        
        // ===== 备用：通过标签查找 =====
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (var player in players)
        {
            RoomPlayer rp = player.GetComponent<RoomPlayer>();
            if (rp != null && rp.netId == localPlayer.NetId)
            {
                target = player.transform;
                isFollowing = true;
                Debug.Log($"✅ 相机跟随本地玩家(备用): {localPlayer.PlayerName}");
                return;
            }
        }
        
        Debug.LogWarning($"⚠️ 未找到 NetId {localPlayer.NetId} 对应的玩家");
    }
}