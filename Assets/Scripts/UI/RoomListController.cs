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
            listStatusText.text = "点击「刷新」搜索局域网房间";
        
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
        UpdateRoomList(roomList);
    }
    
    void OnRoomError(RoomError error)
    {
        if (error.op == RoomOp.Refresh || error.op == RoomOp.Join)
        {
            string errorMsg = error.reason switch
            {
                RoomErrorReason.Timeout => "⏰ 操作超时",
                RoomErrorReason.RoomNotFound => "🔍 房间不存在",
                RoomErrorReason.RoomFull => "👥 房间已满",
                RoomErrorReason.ConnectionFailed => "🔌 网络连接失败",
                RoomErrorReason.AlreadyInRoom => "⚠️ 已在房间中",
                _ => $"❌ 操作失败：{error.message}"
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
        ClearRoomList();
        
        // ===== 优先使用契约 =====
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.RefreshRoomList();
            if (listStatusText != null)
                listStatusText.text = "🔍 正在搜索局域网房间...";
        }
        else
        {
            // 兼容旧版
            if (manualDiscovery != null)
            {
                manualDiscovery.StopListening();
                manualDiscovery.StartListening();
                if (listStatusText != null)
                    listStatusText.text = "🔍 正在搜索局域网房间...";
            }
            else
            {
                if (listStatusText != null)
                    listStatusText.text = "❌ 错误：未找到 ManualDiscovery 组件！";
            }
        }
        
        if (searchInputField != null)
            searchInputField.text = "";
    }
    
    // ===== 使用契约中的 RoomInfo =====
    public void AddRoom(string serverId, string ipAddress, int port, string roomName, 
                        string hostName, int currentPlayers, int maxPlayers, 
                        RoomStatus status, string gameMode = "经典模式", float ping = -1f)
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
                    item.playerCountText.text = $"{currentPlayers}/{updated.maxPlayers}人";
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
            listStatusText.text = "📭 没有找到任何房间，点击「刷新」搜索";
        }
        else if (displayedRooms.Count == 0)
        {
            listStatusText.text = $"🔍 没有匹配的房间（共 {allRooms.Count} 个）";
        }
        else
        {
            listStatusText.text = $"✅ 找到 {allRooms.Count} 个房间 | 🟢空闲:{idleCount} 🟡游戏中:{playingCount}";
        }
    }
    
    public void JoinRoom(RoomItemData roomData)
    {
        if (netManager == null)
        {
            if (listStatusText != null)
                listStatusText.text = "❌ 错误：找不到网络管理器！";
            return;
        }
        
        bool isObserver = (roomData.status != RoomStatus.Idle);
        
        if (isObserver)
        {
            if (listStatusText != null)
                listStatusText.text = $"👀 以观战模式加入 {roomData.roomName}...";
            Debug.Log($"以观战模式加入房间：{roomData.roomName}");
        }
        else
        {
            if (listStatusText != null)
                listStatusText.text = $"🎮 以玩家身份加入 {roomData.roomName}...";
            Debug.Log($"以玩家身份加入房间：{roomData.roomName}");
        }
        
        // ===== 优先使用契约 =====
        if (GameContract.IsRoomBound)
        {
            GameContract.RoomCommands.JoinRoom(roomData.serverId);
            if (listStatusText != null)
                listStatusText.text = $"⏳ 正在加入 {roomData.roomName}...";
        }
        else
        {
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
        yield return new WaitForSeconds(0.5f);
        
        netManager.networkAddress = roomData.ipAddress;
        PlayerPrefs.SetInt("IsObserver", isObserver ? 1 : 0);
        netManager.StartClient();
        
        if (listStatusText != null)
            listStatusText.text = $"⏳ 正在连接 {roomData.ipAddress}...";
    }
    
    void OnSortChanged(int index)
    {
        ApplyFiltersAndSort();
        if (listStatusText != null && sortDropdown != null)
            listStatusText.text = $"📊 已按「{sortDropdown.options[index].text}」排序";
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
                listStatusText.text = $"🔍 搜索: \"{searchText}\" 结果: {displayedRooms.Count} 个房间";
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