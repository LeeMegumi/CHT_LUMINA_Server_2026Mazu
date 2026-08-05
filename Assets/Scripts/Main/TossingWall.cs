using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TossingWall : MonoBehaviour
{
    [Header("Canvas Settings")]
    [SerializeField] private bool isFirstCanvas = true; // true = 1-30, false = 31-60

    [Header("Grid Settings")]
    [SerializeField] private int columns = 5; // 橫排5個
    [SerializeField] private int rows = 6; // 豎排6個
    [SerializeField] private Vector2 cellSize = new Vector2(185f, 185f); // 調整為適合1080x1920
    [SerializeField] private Vector2 spacing = new Vector2(18f, 18f); // 調整間距

    [Header("Prefab Settings")]
    [SerializeField] private GameObject cellPrefab; // 你的通用物件Prefab（需包含Animator）

    [Header("Animator Parameters")]
    [SerializeField] private string clearName = "Clear"; // Clear狀態的Trigger名稱
    [SerializeField] private string checkingName = "Checking"; // Checking狀態的Trigger名稱
    [SerializeField] private string confirmName = "Confirm"; // Confirm狀態的Trigger名稱

    private Dictionary<int, GameObject> cellDict = new Dictionary<int, GameObject>();
    private Dictionary<int, Animator> animatorDict = new Dictionary<int, Animator>();
    private Dictionary<int, Text> textDict = new Dictionary<int, Text>();

    public enum WallState
    {
        Clear,
        Checking,
        Confirm
    }

    private WallState currentState = WallState.Clear;
    public int currentCheckingNumber = -1;

    // 編號範圍
    private int startNumber;
    private int endNumber;

    void Start()
    {
        // 根據是第一個還是第二個Canvas設定編號範圍
        if (isFirstCanvas)
        {
            startNumber = 1;
            endNumber = 30;
        }
        else
        {
            startNumber = 31;
            endNumber = 60;
        }

        InitializeWall();
    }

    /// <summary>
    /// 初始化牆面UI
    /// </summary>
    void InitializeWall()
    {
        // 確保 Canvas 設定正確
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // 直式解析度
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
        }

        // 創建Grid容器
        GameObject gridContainer = new GameObject("GridContainer");
        gridContainer.transform.SetParent(transform, false);
        gridContainer.transform.SetSiblingIndex(2);


        RectTransform gridRect = gridContainer.AddComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0.5f, 0.5f);
        gridRect.anchorMax = new Vector2(0.5f, 0.5f);
        gridRect.pivot = new Vector2(0.5f, 0.5f);

        // 添加 GridLayoutGroup
        GridLayoutGroup gridLayout = gridContainer.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = cellSize;
        gridLayout.spacing = spacing;
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = columns;
        gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        gridLayout.startAxis = GridLayoutGroup.Axis.Vertical;
        gridLayout.childAlignment = TextAnchor.MiddleCenter;
        gridLayout.padding.bottom = 5;

        // 創建30個格子物件 (從startNumber到endNumber)
        if (cellPrefab != null)
        {
            for (int i = startNumber; i <= endNumber; i++)
            {
                CreateCellFromPrefab(i, gridContainer.transform);
            }
        }
        else
        {
            Debug.LogError("Cell Prefab is not assigned! Please assign your prefab with Animator in the Inspector.");
            // 如果沒有Prefab，創建基本的Image格子作為後備
            for (int i = startNumber; i <= endNumber; i++)
            {
                CreateBasicImageCell(i, gridContainer.transform);
            }
        }

        // 計算容器大小以適應所有格子
        float totalWidth = (cellSize.x * columns) + (spacing.x * (columns - 1));
        float totalHeight = (cellSize.y * rows) + (spacing.y * (rows - 1));
        gridRect.sizeDelta = new Vector2(totalWidth, totalHeight);

        string canvasName = isFirstCanvas ? "Canvas 1-30" : "Canvas 31-60";
        Debug.Log($"{canvasName} initialized: Grid size {totalWidth}x{totalHeight}, Numbers {startNumber}-{endNumber}");
    }

    /// <summary>
    /// 使用Prefab創建格子
    /// </summary>
    void CreateCellFromPrefab(int number, Transform parent)
    {
        GameObject cell = Instantiate(cellPrefab, parent);
        cell.name = number.ToString();

        // 確保有RectTransform
        RectTransform rectTransform = cell.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = cell.AddComponent<RectTransform>();
        }

        // 獲取Animator組件
        Animator animator = cell.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"Cell {number} doesn't have an Animator component! Adding one, but please assign an AnimatorController.");
            animator = cell.AddComponent<Animator>();
        }

        cellDict.Add(number, cell);
        animatorDict.Add(number, animator);

        // 尋找或創建Text組件來顯示編號（可選）
        Text text = cell.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = number.ToString();
            textDict.Add(number, text);
        }
        animator.Play(clearName);
    }

    /// <summary>
    /// 創建基本Image格子（後備方案，如果沒有Prefab）
    /// </summary>
    void CreateBasicImageCell(int number, Transform parent)
    {
        GameObject cell = new GameObject(number.ToString());
        cell.transform.SetParent(parent, false);

        // 添加Image組件
        Image image = cell.AddComponent<Image>();
        image.color = new Color(100f / 255f, 100f / 255f, 100f / 255f, 1f);

        // 添加Animator
        Animator animator = cell.AddComponent<Animator>();

        cellDict.Add(number, cell);
        animatorDict.Add(number, animator);

        // 創建Text子物件顯示編號
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(cell.transform, false);

        Text text = textObj.AddComponent<Text>();
        text.text = number.ToString();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.fontStyle = FontStyle.Bold;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        textDict.Add(number, text);
    }

    /// <summary>
    /// 設置為Clear狀態 - 播放Clear動畫
    /// </summary>
    public void SetClearState()
    {
        currentState = WallState.Clear;
        currentCheckingNumber = -1;

        foreach (var kvp in animatorDict)
        {
            kvp.Value.Play(clearName);
        }
    }

    /// <summary>
    /// 設置為Checking狀態 - 指定編號播放Checking動畫
    /// </summary>
    public void SetCheckingState(int number)
    {
        // 檢查編號是否在此Canvas的範圍內
        if (number < startNumber || number > endNumber)
        {
            Debug.LogWarning($"Number {number} is not in this canvas range ({startNumber}-{endNumber})");
            return;
        }

        if (!animatorDict.ContainsKey(number))
        {
            Debug.LogWarning($"Invalid number: {number}");
            return;
        }

        currentState = WallState.Checking;
        currentCheckingNumber = number;
        animatorDict[number].Play(checkingName);
    }

    /// <summary>
    /// 設置為Confirm狀態 - 播放Confirm動畫
    /// </summary>
    public void SetConfirmState()
    {
        currentState = WallState.Confirm;
        animatorDict[currentCheckingNumber].Play(confirmName);

    }

    /// <summary>
    /// 檢查指定編號是否屬於此Canvas
    /// </summary>
    public bool ContainsNumber(int number)
    {
        return number >= startNumber && number <= endNumber;
    }

    /// <summary>
    /// 獲取指定編號的Animator（如需手動控制）
    /// </summary>
    public Animator GetAnimator(int number)
    {
        if (animatorDict.ContainsKey(number))
        {
            return animatorDict[number];
        }
        return null;
    }

    /// <summary>
    /// 獲取指定編號的GameObject（如需手動控制）
    /// </summary>
    public GameObject GetCell(int number)
    {
        if (cellDict.ContainsKey(number))
        {
            return cellDict[number];
        }
        return null;
    }

    // ========== 測試用公開方法 ==========

    [ContextMenu("Test Clear")]
    public void TestClear()
    {
        SetClearState();
    }

    [ContextMenu("Test Checking First Number")]
    public void TestCheckingFirst()
    {
        SetCheckingState(startNumber);
    }

    [ContextMenu("Test Checking Middle Number")]
    public void TestCheckingMiddle()
    {
        int middleNumber = (startNumber + endNumber) / 2;
        SetCheckingState(middleNumber);
    }

    [ContextMenu("Test Confirm")]
    public void TestConfirm()
    {
        SetConfirmState();
    }
}
