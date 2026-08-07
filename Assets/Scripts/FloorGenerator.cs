using System.Collections;
using UnityEngine;

public class FloorGenerator : MonoBehaviour
{
    [Header("地板 Prefab")]
    public GameObject floorPrefab;

    [Header("地板尺寸設定（單位：格）")]
    public int floorWidth = 5;
    public int floorDepth = 5;

    [Header("轉場動畫設定")]
    [Tooltip("每排開始翻轉的間隔時間（秒）")]
    public float rowDelay = 0.12f;
    [Tooltip("每塊地板翻轉動畫持續時間（秒）")]
    public float flipDuration = 0.45f;
    [Tooltip("翻轉目標角度（X 軸）")]
    public float targetXAngle = -90f;

    [Header("材質切換設定")]
    [Tooltip("翻轉後要套用的目標材質球")]
    public Material[] targetMaterial;
    public int nextMatIndex;
    [Tooltip("換材質球的時機（動畫進度 0~1，建議 0.5）")]
    [Range(0f, 1f)]
    public float swapProgress = 0.5f;

    private const float TileSize = 10f;
    private const string AlphaProperty = "_Alpha";

    private GameObject[,] tiles;

    private void Update()
    {
        if(Input.GetKeyUp(KeyCode.F))
        {
            PlayFlipAnimation();
        }
    }
    // ── 啟動 ─────────────────────────────────────────
    private void Start()
    {
        GenerateFloor();
    }

    // ── 生成地板 ──────────────────────────────────────
    [ContextMenu("Generate Floor")]
    public void GenerateFloor()
    {
        ClearFloor();

        if (floorPrefab == null)
        {
            Debug.LogError("[FloorGenerator] 請先指定 floorPrefab！");
            return;
        }

        tiles = new GameObject[floorWidth, floorDepth];

        float offsetX = (floorWidth - 1) * TileSize / 2f;
        float offsetZ = (floorDepth - 1) * TileSize / 2f;

        for (int z = 0; z < floorDepth; z++)
        {
            for (int x = 0; x < floorWidth; x++)
            {
                Vector3 localPos = new Vector3(
                    x * TileSize - offsetX,
                    0f,
                    z * TileSize - offsetZ
                );

                GameObject tile = Instantiate(
                    floorPrefab,
                    transform.TransformPoint(localPos),
                    transform.rotation,
                    transform
                );

                tile.name = $"Tile_{x}_{z}";
                tiles[x, z] = tile;

                // 確保初始 Alpha 為 1
                SetTileAlpha(tile, 1f);
            }
        }

        Debug.Log($"[FloorGenerator] 生成完成：{floorWidth} x {floorDepth} 格，共 {floorWidth * floorDepth} 塊。");
    }

    // ── 播放翻轉轉場動畫 ──────────────────────────────
    [ContextMenu("Play Flip Animation")]
    public void PlayFlipAnimation()
    {
        targetXAngle -= 90;
        nextMatIndex += 1;
        if (tiles == null || tiles.Length == 0)
        {
            Debug.LogWarning("[FloorGenerator] 尚未生成地板，請先執行 Generate Floor。");
            return;
        }

        if (targetMaterial == null)
        {
            Debug.LogWarning("[FloorGenerator] 尚未指定 targetMaterial，將只執行旋轉動畫。");
        }

        StopAllCoroutines();
        StartCoroutine(FlipRowsCoroutine());
        
    }

    private IEnumerator FlipRowsCoroutine()
    {
        for (int z = floorDepth - 1; z >= 0; z--)
        {
            for (int x = 0; x < floorWidth; x++)
            {
                if (tiles[x, z] != null)
                    StartCoroutine(FlipTileCoroutine(tiles[x, z]));
            }

            yield return new WaitForSeconds(rowDelay);
        }
        
    }

    private IEnumerator FlipTileCoroutine(GameObject tile)
    {
        Renderer rend = tile.GetComponent<Renderer>();
        Quaternion startRot = tile.transform.localRotation;
        Quaternion endRot = Quaternion.Euler(targetXAngle, 0f, 0f);
        float elapsed = 0f;
        bool swapped = false;

        // 前半段淡出範圍：0 → swapProgress
        // 後半段淡入範圍：swapProgress → 1
        while (elapsed < flipDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flipDuration);

            // ── 階段一：淡出舊材質 Alpha 1 → 0 ──────
            if (t <= swapProgress)
            {
                float fadeOutT = t / swapProgress; // 重新映射到 0~1
                float alpha = Mathf.Lerp(1f, 0f, EaseInCubic(fadeOutT));
                SetTileAlpha(tile, alpha);
            }

            // ── 階段二：Alpha 最低點時換材質球 ────────
            if (!swapped && t >= swapProgress)
            {
                if (targetMaterial != null)
                {
                    rend.material = targetMaterial[nextMatIndex%3];  // 換材質球（此時 Alpha ≈ 0，視覺無感）
                }
                SetTileAlpha(tile, 0f);              // 確保新材質從 Alpha=0 開始
                swapped = true;
            }

            // ── 階段三：淡入新材質 Alpha 0 → 1 ───────
            if (swapped && t >= swapProgress)
            {
                float fadeInT = (t - swapProgress) / (1f - swapProgress); // 重新映射到 0~1
                float alpha = Mathf.Lerp(0f, 1f, EaseOutCubic(fadeInT));
                SetTileAlpha(tile, alpha);
            }

            // ── 旋轉（全程執行）────────────────────────
            tile.transform.localRotation = Quaternion.Lerp(startRot, endRot, EaseOutCubic(t));

            yield return null;
        }

        // 確保最終狀態精確
        tile.transform.localRotation = endRot;
        SetTileAlpha(tile, 1f);
    }

    // ── 設定單一 Tile 的 Alpha 值 ─────────────────────
    private void SetTileAlpha(GameObject tile, float alpha)
    {
        Renderer rend = tile.GetComponent<Renderer>();
        if (rend == null) return;

        // 使用 MaterialPropertyBlock，不產生額外 Material Instance
        var block = new MaterialPropertyBlock();
        rend.GetPropertyBlock(block);
        block.SetFloat(AlphaProperty, alpha);
        rend.SetPropertyBlock(block);
    }

    // ── 重置所有地板 ──────────────────────────────────
    [ContextMenu("Reset Floor Rotation")]
    public void ResetFloorRotation()
    {
        StopAllCoroutines();
        if (tiles == null) return;

        for (int z = 0; z < floorDepth; z++)
        {
            for (int x = 0; x < floorWidth; x++)
            {
                if (tiles[x, z] != null)
                {
                    tiles[x, z].transform.localRotation = Quaternion.identity;
                    SetTileAlpha(tiles[x, z], 1f);
                }
            }
        }
    }

    // ── 清空地板 ──────────────────────────────────────
    [ContextMenu("Clear Floor")]
    public void ClearFloor()
    {
        StopAllCoroutines();
        tiles = null;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
#if UNITY_EDITOR
            DestroyImmediate(transform.GetChild(i).gameObject);
#else
            Destroy(transform.GetChild(i).gameObject);
#endif
        }
    }

    // ── Easing Functions ──────────────────────────────
    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    private static float EaseInCubic(float t) => t * t * t;
}