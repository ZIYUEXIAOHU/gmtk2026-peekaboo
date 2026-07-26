using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

public class RoomListController : MonoBehaviour
{
    [Header("UI组件")]
    public Transform roomListParent;
    public GameObject roomItemPrefab;
    public TextMeshProUGUI listStatusText;
    
    [Header("功能栏")]
    public Button refreshBtn;
    public Dropdown sortDropdown;
    public TMP_InputField searchInputField;
    public Button searchConfirmBtn;
    
    [Header("主控制器")]
    public MainMenuController mainMenuController;
    
    // ===== 使用契约中的 RoomInfo =====
    private List<RoomInfo> allRooms = new List<RoomInfo>();
    private List<RoomInfo> displayedRooms = new List<RoomInfo>();
    private Dictionary<string, RoomItemUI> roomItemMap = new Dictionary<string, RoomItemUI>();
    
    private CustomNetworkManager netManager;
    private ManualDiscovery manualDiscovery;
    
    private enum SortMode
    {
        Default,
        MostPlayers,
        LeastPlayers,
        Name,
        Status
    }
    
    void Start()
    {
        netManager = FindObjectOfType<CustomNetworkManager>();
        manualDiscovery = FindObjectOfType<ManualDiscovery>();
        
        if (refreshBtn == null)
            Debug.LogError("❌ RefreshBtn 未绑定！");
        if (sortDropdown == null)
            Debug.LogError("❌ SortDropdown 未绑定！");
        if (searchInputField == null)
            Debug.LogError("❌ SearchInputField 未绑定！");
        if (searchConfirmBtn == null)
            Debug.LogError("❌ SearchConfirmBtn 未绑定！");
        if (listStatusText == null)
            Debug.LogError("❌ ListStatusText 未绑定！");
        if (roomListParent == null)
            Debug.LogError("❌ RoomListParent 未绑定！");
        if (roomItemPrefab == null)
            Debug.LogError("❌ RoomItemPrefab 未绑定！");
        
        if (refreshBtn != null)
            refreshBtn.onClick.AddListener(RefreshRoomList);
        
        if (sortDropdown != null)
            sortDropdown.onValueChanged.AddListener(OnSortChanged);
        
        if (searchInputField != null)
            searchInputField.onEndEdit.AddListener(OnSearchEndEdit);
        
        if (searchConfirmBtn != null)
            searchConfirmBtn.onClick.AddListener(OnSearchConfirm);
        
        if (listStatusText != null)
            listStatusText.text = "Tap Refresh to search for LAN rooms";
        
        // ===== 订阅契约事件 =====
        SubscribeRoomEvents();
    }
    
    // ==================== 订阅契约事件 ====================
    void SubscribeRoomEvents()
    {
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnRoomListUpdated += OnRoomListUpdated;
                GameContract.RoomEvents.OnRoomError += OnRoomError;
                Debug.Log("✅ RoomListController 订阅契约事件成功");
            }
            else
            {
                Debug.Log("⏳ 等待契约绑定...");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"订阅契约事件失败（等待契约实现）：{e.Message}");
        }
    }
    
    // ==================== 契约事件回调 ====================
    void OnRoomListUpdated(IReadOnlyList<RoomInfo> roomList)
    {
        Debug.Log($"📋 收到房间列表更新：{roomList.Count} 个房间");
        UpdateRoomList(roomList);
    }
    
    void OnRoomError(RoomError error)
    {
        Debug.Log($"❌ 房间错误: {error.op} - {error.reason}");
        if (error.op == RoomOp.Refresh || error.op == RoomOp.Join)
        {
            string errorMsg = error.reason switch
            {
                RoomErrorReason.Timeout => "⏰ Operation timed out",
                RoomErrorReason.RoomNotFound => "🔍 Room not found",
                RoomErrorReason.RoomFull => "👥 Room is full",
                RoomErrorReason.ConnectionFailed => "🔌 Network connection failed",
                RoomErrorReason.AlreadyInRoom => "⚠️ Already in a room",
                _ => $"❌ Operation failed: {error.message}"
            };
            
            if (listStatusText != null)
                listStatusText.text = errorMsg;
        }
    }
    
    // ==================== 更新房间列表（供契约调用） ====================
    public void UpdateRoomList(IReadOnlyList<RoomInfo> roomList)
    {
        ClearRoomList();
        
        foreach (var room in roomList)
        {
            allRooms.Add(room);
        }
        
        ApplyFiltersAndSort();
        UpdateStatusText();
    }
    
    public void RefreshRoomList()
    {
        Debug.Log("🔄 刷新房间列表");
        ClearRoomList();
        
        // ===== 优先使用契约 =====
        if (GameContract.IsRoomBound)
        {
            Debug.Log("📡 使用契约刷新房间列表");
            GameContract.RoomCommands.RefreshRoomList();
            if (listStatusText != null)
                listStatusText.text = "🔍 Searching for LAN rooms...";
        }
        else
        {
            Debug.Log("📡 使用兼容模式刷新房间列表");
            // 兼容旧版
            if (manualDiscovery != null)
            {
                manualDiscovery.StopListening();
                manualDiscovery.StartListening();
                if (listStatusText != null)
                    listStatusText.text = "🔍 Searching for LAN rooms...";
            }
            else
            {
                if (listStatusText != null)
                    listStatusText.text = "❌ ManualDiscovery component not found";
            }
        }
        
        if (searchInputField != null)
            searchInputField.text = "";
    }
    
    // ===== 使用契约中的 RoomInfo =====
    public void AddRoom(string serverId, string ipAddress, int port, string roomName, 
                        string hostName, int currentPlayers, int maxPlayers, 
                        RoomStatus status, string gameMode = "Classic Mode", float ping = -1f)
    {
        if (allRooms.Any(r => r.serverId == serverId))
        {
            UpdateRoom(serverId, currentPlayers, status, ping);
            return;
        }
        
        RoomInfo room = new RoomInfo
        {
            serverId = serverId,
            roomName = roomName,
            hostName = hostName,
            currentPlayers = currentPlayers,
            maxPlayers = maxPlayers,
            status = status,
            ping = ping
        };
        
        allRooms.Add(room);
        ApplyFiltersAndSort();
        UpdateStatusText();
    }
    
    public void UpdateRoom(string serverId, int currentPlayers, RoomStatus status, float ping = -1f)
    {
        int index = allRooms.FindIndex(r => r.serverId == serverId);
        if (index >= 0)
        {
            RoomInfo updated = allRooms[index];
            updated.currentPlayers = currentPlayers;
            updated.status = status;
            if (ping >= 0f)
                updated.ping = ping;
            allRooms[index] = updated;
            
            if (roomItemMap.ContainsKey(serverId))
            {
                roomItemMap[serverId].UpdateStatus(status);
                RoomItemUI item = roomItemMap[serverId];
                if (item.playerCountText != null)
                {
                    item.playerCountText.text = $"{currentPlayers}/{updated.maxPlayers} players";
                }
            }
            
            ApplyFiltersAndSort();
        }
    }
    
    public void RemoveRoom(string serverId)
    {
        allRooms.RemoveAll(r => r.serverId == serverId);
        if (roomItemMap.ContainsKey(serverId))
        {
            Destroy(roomItemMap[serverId].gameObject);
            roomItemMap.Remove(serverId);
        }
        ApplyFiltersAndSort();
        UpdateStatusText();
    }
    
    public void ClearRoomList()
    {
        allRooms.Clear();
        displayedRooms.Clear();
        roomItemMap.Clear();
        
        foreach (Transform child in roomListParent)
        {
            Destroy(child.gameObject);
        }
    }
    
    public void ApplyFiltersAndSort()
    {
        if (sortDropdown == null || searchInputField == null)
        {
            Debug.LogWarning("sortDropdown 或 searchInputField 未绑定，跳过排序");
            return;
        }
        
        string searchText = searchInputField.text.Trim().ToLower();
        SortMode sortMode = (SortMode)sortDropdown.value;
        
        IEnumerable<RoomInfo> filtered = allRooms;
        
        if (!string.IsNullOrEmpty(searchText))
        {
            filtered = filtered.Where(r => 
                r.roomName.ToLower().Contains(searchText) ||
                r.hostName.ToLower().Contains(searchText)
            );
        }
        
        displayedRooms = SortRooms(filtered.ToList(), sortMode);
        UpdateRoomListUI();
    }
    
    List<RoomInfo> SortRooms(List<RoomInfo> rooms, SortMode mode)
    {
        switch (mode)
        {
            case SortMode.MostPlayers:
                return rooms.OrderByDescending(r => r.currentPlayers).ToList();
            case SortMode.LeastPlayers:
                return rooms.OrderBy(r => r.currentPlayers).ToList();
            case SortMode.Name:
                return rooms.OrderBy(r => r.roomName).ToList();
            case SortMode.Status:
                return rooms.OrderBy(r => r.status).ToList();
            case SortMode.Default:
            default:
                return rooms.ToList();
        }
    }
    
    void UpdateRoomListUI()
    {
        List<string> currentKeys = new List<string>(roomItemMap.Keys);
        foreach (string key in currentKeys)
        {
            if (!displayedRooms.Any(r => r.serverId == key))
            {
                Destroy(roomItemMap[key].gameObject);
                roomItemMap.Remove(key);
            }
        }
        
        foreach (RoomInfo room in displayedRooms)
        {
            if (roomItemMap.ContainsKey(room.serverId))
                continue;
            
            GameObject item = Instantiate(roomItemPrefab, roomListParent);
            RoomItemUI itemUI = item.GetComponent<RoomItemUI>();
            
            if (itemUI != null)
            {
                // 将 RoomInfo 转换为 RoomItemData（兼容旧版 UI）
                RoomItemData data = new RoomItemData
                {
                    serverId = room.serverId,
                    roomName = room.roomName,
                    hostName = room.hostName,
                    currentPlayers = room.currentPlayers,
                    maxPlayers = room.maxPlayers,
                    status = room.status,
                    ping = room.ping
                };
                itemUI.SetRoomData(data, this);
                roomItemMap[room.serverId] = itemUI;
            }
        }
        
        UpdateStatusText();
    }
    
    void UpdateStatusText()
    {
        if (listStatusText == null)
            return;
        
        int idleCount = allRooms.Count(r => r.status == RoomStatus.Idle);
        int playingCount = allRooms.Count(r => r.status == RoomStatus.Playing);
        
        if (allRooms.Count == 0)
        {
            listStatusText.text = "📭 No rooms found. Tap Refresh to search";
        }
        else if (displayedRooms.Count == 0)
        {
            listStatusText.text = $"🔍 No matching rooms ({allRooms.Count} total)";
        }
        else
        {
            listStatusText.text = $"✅ Found {allRooms.Count} rooms | 🟢Idle:{idleCount} 🟡In Game:{playingCount}";
        }
    }
    
    public void JoinRoom(RoomItemData roomData)
    {
        Debug.Log($"🔵 JoinRoom 被调用！roomData: {roomData.roomName}, serverId: {roomData.serverId}, ip: {roomData.ipAddress}");
        
        if (netManager == null)
        {
            Debug.LogError("❌ netManager 为空！");
            if (listStatusText != null)
                listStatusText.text = "❌ Network manager not found";
            return;
        }
        
        bool isObserver = (roomData.status != RoomStatus.Idle);
        
        if (isObserver)
        {
            if (listStatusText != null)
                listStatusText.text = $"👀 Joining {roomData.roomName} as spectator...";
            Debug.Log($"👀 以观战模式加入房间：{roomData.roomName}");
        }
        else
        {
            if (listStatusText != null)
                listStatusText.text = $"🎮 Joining {roomData.roomName} as player...";
            Debug.Log($"🎮 以玩家身份加入房间：{roomData.roomName}");
        }
        
        // ===== 打印契约绑定状态 =====
        Debug.Log($"🔍 GameContract.IsRoomBound: {GameContract.IsRoomBound}");
        
        // ===== 优先使用契约 =====
        if (GameContract.IsRoomBound)
        {
            Debug.Log($"📡 使用契约加入房间: {roomData.serverId}");
            GameContract.RoomCommands.JoinRoom(roomData.serverId);
            if (listStatusText != null)
                listStatusText.text = $"⏳ Joining {roomData.roomName}...";
        }
        else
        {
            Debug.Log($"📡 使用兼容模式加入房间: {roomData.ipAddress}");
            // 兼容旧版
            if (NetworkServer.active)
                netManager.StopHost();
            if (NetworkClient.active)
                netManager.StopClient();
            
            StartCoroutine(DelayedConnect(roomData, isObserver));
        }
    }
    
    private IEnumerator DelayedConnect(RoomItemData roomData, bool isObserver)
    {
        Debug.Log($"⏳ DelayedConnect 开始，等待 0.5 秒...");
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log($"📡 正在连接 IP: {roomData.ipAddress}");
        netManager.networkAddress = roomData.ipAddress;
        PlayerPrefs.SetInt("IsObserver", isObserver ? 1 : 0);
        netManager.StartClient();
        
        if (listStatusText != null)
            listStatusText.text = $"⏳ Connecting {roomData.ipAddress}...";
    }
    
    void OnSortChanged(int index)
    {
        ApplyFiltersAndSort();
        if (listStatusText != null && sortDropdown != null)
            listStatusText.text = $"📊 Sorted by \"{sortDropdown.options[index].text}\"";
    }
    
    void OnSearchEndEdit(string searchText)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ApplyFiltersAndSort();
            UpdateSearchStatus(searchText);
        }
    }
    
    void OnSearchConfirm()
    {
        string searchText = searchInputField != null ? searchInputField.text : "";
        ApplyFiltersAndSort();
        UpdateSearchStatus(searchText);
    }
    
    void UpdateSearchStatus(string searchText)
    {
        if (!string.IsNullOrEmpty(searchText))
        {
            if (listStatusText != null)
                listStatusText.text = $"🔍 Search: \"{searchText}\" Results: {displayedRooms.Count} rooms";
        }
        else
        {
            UpdateStatusText();
        }
    }
    
    void OnDestroy()
    {
        CancelInvoke("AutoRefresh");
        
        try
        {
            if (GameContract.IsRoomBound)
            {
                GameContract.RoomEvents.OnRoomListUpdated -= OnRoomListUpdated;
                GameContract.RoomEvents.OnRoomError -= OnRoomError;
            }
        }
        catch { }
    }
}