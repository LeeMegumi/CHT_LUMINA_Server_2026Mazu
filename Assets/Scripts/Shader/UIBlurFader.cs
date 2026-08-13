using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 用程式碼控制 UIBlur / UIFrostedGlass 材質的淡入淡出。
///
/// 為什麼不能用 Animator：
/// UIFrostedGlass 是 ShaderGraph (HDRP Lit)，透明度來自材質的 _Tint.a，
/// 不是 Graphic.color，所以 CanvasGroup / RawImage.color / Animator 的
/// Color 曲線都動不到它；Animator 也無法直接對「材質屬性」錄製曲線。
/// 這支腳本在執行時複製一份材質實體 (instance)，逐格改 _Tint 的 alpha。
///
/// 用法：掛在 Canvas(BG) 上，把 BGBlur_Left / BGBlur_Right 拖進 Targets，
/// 然後從別的腳本呼叫 FadeIn() / FadeOut()。
/// </summary>
[AddComponentMenu("UI/UI Blur Fader")]
[DisallowMultipleComponent]
public class UIBlurFader : MonoBehaviour
{
    [Header("目標")]
    [Tooltip("要淡入淡出的 RawImage / Image。留空時會自動抓自己與子物件上的 Graphic。")]
    [SerializeField] private Graphic[] targets;

    [Header("材質參數")]
    [Tooltip("ShaderGraph 中的顏色屬性名稱，UIFrostedGlass 是 _Tint")]
    [SerializeField] private string colorPropertyName = "_Tint";

    [Tooltip("勾選時，以材質原本的 alpha 當作『完全顯示』的值 (UIBlur.mat 目前是 0.749)")]
    [SerializeField] private bool useMaterialAlphaAsMax = true;

    [Range(0f, 1f)]
    [Tooltip("完全顯示時的 alpha。useMaterialAlphaAsMax 勾選時會在 Awake 被覆寫。")]
    [SerializeField] private float maxAlpha = 0.75f;

    [Header("動畫")]
    [Min(0f)][SerializeField] private float fadeInDuration = 0.5f;
    [Min(0f)][SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("使用不受 Time.timeScale 影響的時間 (UI 建議開啟)")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("啟動設定")]
    [Tooltip("Awake 時先設為完全透明")]
    [SerializeField] private bool hiddenOnAwake = true;
    [Tooltip("OnEnable 時自動淡入")]
    [SerializeField] private bool playFadeInOnEnable = false;
    [Tooltip("完全透明時關掉 Graphic 元件，省下模糊材質的繪製成本")]
    [SerializeField] private bool disableGraphicWhenHidden = true;

    [Header("事件")]
    public UnityEvent onFadeInComplete;
    public UnityEvent onFadeOutComplete;

    private Material[] instances;
    private int propId;
    private Coroutine routine;
    private float current;      // 目前實際 alpha
    private float pendingGoal;  // 動畫中的目標 alpha

    /// <summary>目前的顯示程度 0~1 (已正規化，與 maxAlpha 無關)</summary>
    public float CurrentAmount => maxAlpha <= 0f ? 0f : Mathf.Clamp01(current / maxAlpha);
    public bool IsVisible => current > 0.0001f;
    public bool IsPlaying => routine != null;

    // ───────────────────────────── 生命週期 ─────────────────────────────

    private void Awake()
    {
        propId = Shader.PropertyToID(colorPropertyName);

        if (targets == null || targets.Length == 0)
            targets = GetComponentsInChildren<Graphic>(true);

        instances = new Material[targets.Length];

        for (int i = 0; i < targets.Length; i++)
        {
            var g = targets[i];
            if (g == null) continue;

            var src = g.material;
            if (src == null)
            {
                Debug.LogWarning($"[UIBlurFader] {g.name} 沒有指定材質，略過。", g);
                continue;
            }

            // 複製材質實體，避免直接改到磁碟上的 UIBlur.mat（那會在編輯器裡被存檔）
            var inst = new Material(src) { name = src.name + " (BlurFader Instance)" };
            g.material = inst;
            instances[i] = inst;

            if (!inst.HasProperty(propId))
                Debug.LogWarning($"[UIBlurFader] 材質 {src.name} 沒有屬性 {colorPropertyName}。", g);
            else if (useMaterialAlphaAsMax && i == 0)
                maxAlpha = inst.GetColor(propId).a;
        }

        ApplyAlpha(hiddenOnAwake ? 0f : maxAlpha);
    }

    private void OnEnable()
    {
        if (playFadeInOnEnable) FadeIn();
    }

    private void OnDisable()
    {
        // 物件被關掉時 Coroutine 會中斷，直接跳到目標值避免卡在中間
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
            ApplyAlpha(pendingGoal);
        }
    }

    private void OnDestroy()
    {
        if (instances == null) return;
        foreach (var m in instances)
        {
            if (m == null) continue;
            if (Application.isPlaying) Destroy(m);
            else DestroyImmediate(m);
        }
    }

    // ───────────────────────────── 對外 API ─────────────────────────────

    /// <summary>淡入到完全顯示</summary>
    public void FadeIn() => FadeIn(fadeInDuration);

    public void FadeIn(float duration)
    {
        StartFade(maxAlpha, duration, onFadeInComplete);
    }

    /// <summary>淡出到完全透明</summary>
    public void FadeOut() => FadeOut(fadeOutDuration);

    public void FadeOut(float duration)
    {
        StartFade(0f, duration, onFadeOutComplete);
    }

    /// <summary>依目前狀態自動切換</summary>
    public void Toggle()
    {
        if (pendingGoal > 0f || (routine == null && IsVisible)) FadeOut();
        else FadeIn();
    }

    /// <summary>true 淡入 / false 淡出</summary>
    public void SetVisible(bool visible)
    {
        if (visible) FadeIn(); else FadeOut();
    }

    /// <summary>淡到指定的顯示程度 (0~1，會自動乘上 maxAlpha)</summary>
    public void FadeTo(float amount01, float duration)
    {
        StartFade(Mathf.Clamp01(amount01) * maxAlpha, duration, null);
    }

    /// <summary>不做動畫，直接設定顯示程度 (0~1)</summary>
    public void SetAmountImmediate(float amount01)
    {
        StopFade();
        ApplyAlpha(Mathf.Clamp01(amount01) * maxAlpha);
    }

    public void ShowImmediate() => SetAmountImmediate(1f);
    public void HideImmediate() => SetAmountImmediate(0f);

    /// <summary>中斷目前的淡入淡出，停在當下的值</summary>
    public void StopFade()
    {
        if (routine == null) return;
        StopCoroutine(routine);
        routine = null;
        pendingGoal = current;
    }

    /// <summary>要 await / yield 等它播完時用：yield return fader.FadeInRoutine();</summary>
    public Coroutine FadeInRoutine() { FadeIn(); return routine; }
    public Coroutine FadeOutRoutine() { FadeOut(); return routine; }

    // ───────────────────────────── 內部 ─────────────────────────────

    private void StartFade(float goal, float duration, UnityEvent onComplete)
    {
        if (instances == null) Awake(); // 還沒 Awake 就被呼叫時的保險

        StopFade();
        pendingGoal = goal;

        if (!isActiveAndEnabled || duration <= 0f)
        {
            ApplyAlpha(goal);
            onComplete?.Invoke();
            return;
        }

        routine = StartCoroutine(FadeRoutine(goal, duration, onComplete));
    }

    private IEnumerator FadeRoutine(float goal, float duration, UnityEvent onComplete)
    {
        float from = current;
        float t = 0f;

        while (t < duration)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = fadeCurve.Evaluate(Mathf.Clamp01(t / duration));
            ApplyAlpha(Mathf.LerpUnclamped(from, goal, k));
            yield return null;
        }

        ApplyAlpha(goal);
        routine = null;
        onComplete?.Invoke();
    }

    private void ApplyAlpha(float a)
    {
        current = a;
        if (instances == null) return;

        bool on = a > 0.0001f;

        for (int i = 0; i < instances.Length; i++)
        {
            var m = instances[i];
            if (m != null && m.HasProperty(propId))
            {
                Color c = m.GetColor(propId);
                c.a = a;
                m.SetColor(propId, c);
            }

            var g = (targets != null && i < targets.Length) ? targets[i] : null;
            if (g == null) continue;

            if (disableGraphicWhenHidden && g.enabled != on) g.enabled = on;
            g.SetMaterialDirty();
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Preview / Fade In")]
    private void ContextFadeIn() { if (Application.isPlaying) FadeIn(); }

    [ContextMenu("Preview / Fade Out")]
    private void ContextFadeOut() { if (Application.isPlaying) FadeOut(); }
#endif
}
