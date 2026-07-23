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
    
    private List<RoomItemData> allRooms = new List<RoomItemData>();
    private List<RoomItemData> displayedRooms = new List<RoomItemData>();
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
    }
    
    public void RefreshRoomList()
    {
        ClearRoomList();
        
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
        
        if (searchInputField != null)
            searchInputField.text = "";
    }
    
    public void AddRoom(string serverId, string ipAddress, int port, string roomName, 
                        string hostName, int currentPlayers, int maxPlayers, 
                        RoomStatus status, string gameMode = "经典模式")
    {
        if (allRooms.Any(r => r.serverId == serverId))
        {
            UpdateRoom(serverId, currentPlayers, status);
            return;
        }
        
        RoomItemData room = new RoomItemData
        {
            serverId = serverId,
            ipAddress = ipAddress,
            port = port,
            roomName = roomName,
            hostName = hostName,
            currentPlayers = currentPlayers,
            maxPlayers = maxPlayers,
            status = status,
            gameMode = gameMode,
            ping = UnityEngine.Random.Range(5f, 50f)
        };
        
        allRooms.Add(room);
        ApplyFiltersAndSort();
        UpdateStatusText();
    }
    
    public void UpdateRoom(string serverId, int currentPlayers, RoomStatus status)
    {
        RoomItemData room = allRooms.Find(r => r.serverId == serverId);
        if (room != null)
        {
            room.currentPlayers = currentPlayers;
            room.status = status;
            
            if (roomItemMap.ContainsKey(serverId))
            {
                roomItemMap[serverId].UpdateStatus(status);
                RoomItemUI item = roomItemMap[serverId];
                if (item.playerCountText != null)
                {
                    item.playerCountText.text = $"{currentPlayers}/{room.maxPlayers}人";
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
        
        IEnumerable<RoomItemData> filtered = allRooms;
        
        if (!string.IsNullOrEmpty(searchText))
        {
            filtered = filtered.Where(r => 
                r.roomName.ToLower().Contains(searchText) ||
                r.hostName.ToLower().Contains(searchText) ||
                r.gameMode.ToLower().Contains(searchText)
            );
        }
        
        displayedRooms = SortRooms(filtered.ToList(), sortMode);
        UpdateRoomListUI();
    }
    
    List<RoomItemData> SortRooms(List<RoomItemData> rooms, SortMode mode)
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
        
        foreach (RoomItemData room in displayedRooms)
        {
            if (roomItemMap.ContainsKey(room.serverId))
                continue;
            
            GameObject item = Instantiate(roomItemPrefab, roomListParent);
            RoomItemUI itemUI = item.GetComponent<RoomItemUI>();
            
            if (itemUI != null)
            {
                itemUI.SetRoomData(room, this);
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
        
        // ===== 先停止所有连接 =====
        if (NetworkServer.active)
            netManager.StopHost();
        if (NetworkClient.active)
            netManager.StopClient();
        
        // ===== 延迟连接，确保完全断开 =====
        StartCoroutine(DelayedConnect(roomData, isObserver));
    }
    
    private IEnumerator DelayedConnect(RoomItemData roomData, bool isObserver)
    {
        yield return new WaitForSeconds(0.5f);
        
        // 设置连接信息
        netManager.networkAddress = roomData.ipAddress;
        
        // 存储观战模式
        PlayerPrefs.SetInt("IsObserver", isObserver ? 1 : 0);
        
        // 启动客户端
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
    }
}