using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// HDRP 天空球自動旋轉。
/// 掛在含有 Volume（且 Profile 內有 HDRI Sky）的物件上，或手動指定 targetVolume。
/// 支援 HDRI Sky / Physically Based Sky / Gradient Sky 的旋轉參數。
/// </summary>
[AddComponentMenu("Rendering/HDRI Sky Rotator")]
[DisallowMultipleComponent]
[ExecuteAlways]
public class HDRISkyRotator : MonoBehaviour
{
    public enum SkyType { Auto, HDRISky, PhysicallyBasedSky }

    [Header("目標 Volume")]
    [Tooltip("留空則自動抓同物件上的 Volume")]
    [SerializeField] private Volume targetVolume;

    [Tooltip("Auto = 自動判斷 Profile 內是哪一種 Sky")]
    [SerializeField] private SkyType skyType = SkyType.Auto;

    [Header("旋轉設定")]
    [Tooltip("每秒旋轉幾度（負數 = 反方向）")]
    [Range(-60f, 60f)]
    [SerializeField] private float rotationSpeed = 1.0f;

    [Tooltip("整體速度倍率，方便用程式做加速 / 減速")]
    [SerializeField] private float speedMultiplier = 1.0f;

    [Tooltip("起始角度（0–360）")]
    [Range(0f, 360f)]
    [SerializeField] private float startRotation = 0f;

    [Header("進階")]
    [Tooltip("勾選後不受 Time.timeScale 影響（暫停時天空仍會轉）")]
    [SerializeField] private bool useUnscaledTime = false;

    [Tooltip("勾選後在 Editor 未播放時也會旋轉（會修改 Profile 資產，請小心）")]
    [SerializeField] private bool previewInEditMode = false;

    [Tooltip("結束時把角度還原成原本的值")]
    [SerializeField] private bool restoreOnDisable = true;

    // --- 內部狀態 ---
    private HDRISky _hdriSky;
    private PhysicallyBasedSky _pbSky;
    private float _currentRotation;
    private float _originalRotation;
    private bool _initialized;

    /// <summary>執行期可直接改速度：rotator.RotationSpeed = 5f;</summary>
    public float RotationSpeed
    {
        get => rotationSpeed;
        set => rotationSpeed = value;
    }

    public float SpeedMultiplier
    {
        get => speedMultiplier;
        set => speedMultiplier = value;
    }

    /// <summary>目前角度（0–360），也可直接指定跳到某個角度。</summary>
    public float CurrentRotation
    {
        get => _currentRotation;
        set
        {
            _currentRotation = Mathf.Repeat(value, 360f);
            Apply(_currentRotation);
        }
    }

    private void OnEnable()
    {
        // Edit Mode 下若沒開預覽就不動 Profile，避免弄髒專案資產。
        if (!Application.isPlaying && !previewInEditMode)
            return;

        Initialize();
    }

    private void OnDisable()
    {
        if (restoreOnDisable && _initialized)
            Apply(_originalRotation);

        _initialized = false;
        _hdriSky = null;
        _pbSky = null;
    }

    private void Initialize()
    {
        if (targetVolume == null)
            targetVolume = GetComponent<Volume>();

        if (targetVolume == null)
        {
            Debug.LogError("[HDRISkyRotator] 找不到 Volume，請把腳本掛在 Volume 物件上，或手動指定 Target Volume。", this);
            enabled = false;
            return;
        }

        // 使用 profile（執行期會複製一份實體），避免直接改到專案裡的 Profile 資產。
        // Edit Mode 預覽時只能用 sharedProfile。
        VolumeProfile profile = Application.isPlaying ? targetVolume.profile : targetVolume.sharedProfile;

        if (profile == null)
        {
            Debug.LogError("[HDRISkyRotator] Volume 沒有指定 Profile。", this);
            enabled = false;
            return;
        }

        if (skyType != SkyType.PhysicallyBasedSky)
            profile.TryGet(out _hdriSky);

        if (skyType != SkyType.HDRISky)
            profile.TryGet(out _pbSky);

        if (_hdriSky == null && _pbSky == null)
        {
            Debug.LogError("[HDRISkyRotator] Volume Profile 裡找不到 HDRI Sky 或 Physically Based Sky，請先在 Profile 加上 Sky Override。", this);
            enabled = false;
            return;
        }

        // 記住原始角度，並確保該參數是 override 狀態，否則寫入不會生效。
        if (_hdriSky != null)
        {
            _hdriSky.rotation.overrideState = true;
            _originalRotation = _hdriSky.rotation.value;
        }
        else
        {
            _pbSky.spaceRotation.overrideState = true;
            _originalRotation = _pbSky.spaceRotation.value.y;
        }

        _currentRotation = Mathf.Repeat(startRotation, 360f);
        _initialized = true;
        Apply(_currentRotation);
    }

    private void Update()
    {
        if (!_initialized)
            return;

        if (!Application.isPlaying && !previewInEditMode)
            return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        _currentRotation = Mathf.Repeat(_currentRotation + rotationSpeed * speedMultiplier * dt, 360f);
        Apply(_currentRotation);
    }

    private void Apply(float degrees)
    {
        if (_hdriSky != null)
        {
            _hdriSky.rotation.value = degrees;
        }
        else if (_pbSky != null)
        {
            Vector3 r = _pbSky.spaceRotation.value;
            r.y = degrees;
            _pbSky.spaceRotation.value = r;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying && previewInEditMode && isActiveAndEnabled && !_initialized)
            Initialize();
    }
#endif
}
