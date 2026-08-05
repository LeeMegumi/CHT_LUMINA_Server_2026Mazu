using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class LuminaCharatorAnimatorController : MonoBehaviour
{
    private Animator animator;

    // --- 動畫名稱陣列 ---
    public string[] SleepidleAnimations;
    public string[] TalkingAnimations;
    public string[] NormalidleAnimations;

    // --- Hash 陣列（提升效能）---
    private int[] SleepidleAnimationHashes;
    private int[] TalkingAnimationHashes;
    private int[] NormalidleAnimationHashes;

    // 融接時間（秒）
    [SerializeField] private float crossFadeDuration = 0.3f;

    // --- 各循環狀態旗標 ---
    public bool sleepIdleLoop = false;
    public bool talkingLoop = false;
    public bool normalIdleLoop = false;

    // --- Coroutine 參考 ---
    private Coroutine sleepIdleCoroutine;
    private Coroutine talkingCoroutine;
    private Coroutine normalIdleCoroutine;
    private Coroutine singleAnimationCoroutine;

    // =========================================================
    // LoopMode 枚舉
    // =========================================================
    public enum LoopMode { None, SleepIdle, Talking, NormalIdle }

    // =========================================================
    // Unity 生命週期
    // =========================================================

    private void Awake()
    {
        animator = GetComponent<Animator>();

        SleepidleAnimationHashes = new int[SleepidleAnimations.Length];
        TalkingAnimationHashes = new int[TalkingAnimations.Length];
        NormalidleAnimationHashes = new int[NormalidleAnimations.Length];
    }

    private void Start()
    {
        for (int i = 0; i < SleepidleAnimations.Length; i++)
            SleepidleAnimationHashes[i] = Animator.StringToHash(SleepidleAnimations[i]);

        for (int i = 0; i < TalkingAnimations.Length; i++)
            TalkingAnimationHashes[i] = Animator.StringToHash(TalkingAnimations[i]);

        for (int i = 0; i < NormalidleAnimations.Length; i++)
            NormalidleAnimationHashes[i] = Animator.StringToHash(NormalidleAnimations[i]);

        
    }

    // =========================================================
    // 公開循環開關屬性
    // =========================================================

    /// <summary> 睡眠待機循環 </summary>
    public bool SleepIdleLoop
    {
        get => sleepIdleLoop;
        set
        {
            sleepIdleLoop = value;
            if (sleepIdleLoop)
            {
                StopAllLoops();
                sleepIdleLoop = true;
                if (sleepIdleCoroutine == null)
                    sleepIdleCoroutine = StartCoroutine(RandomLoopCoroutine(
                        LoopMode.SleepIdle,
                        SleepidleAnimationHashes,
                        SleepidleAnimations,
                        "睡眠待機"));
            }
            else
            {
                StopLoopCoroutine(ref sleepIdleCoroutine);
            }
        }
    }

    /// <summary> 說話動畫循環 </summary>
    public bool TalkingLoop
    {
        get => talkingLoop;
        set
        {
            talkingLoop = value;
            if (talkingLoop)
            {
                StopAllLoops();
                talkingLoop = true;
                if (talkingCoroutine == null)
                    talkingCoroutine = StartCoroutine(RandomLoopCoroutine(
                        LoopMode.Talking,
                        TalkingAnimationHashes,
                        TalkingAnimations,
                        "說話"));
            }
            else
            {
                StopLoopCoroutine(ref talkingCoroutine);
            }
        }
    }

    /// <summary> 一般待機循環 </summary>
    public bool NormalIdleLoop
    {
        get => normalIdleLoop;
        set
        {
            normalIdleLoop = value;
            if (normalIdleLoop)
            {
                StopAllLoops();
                normalIdleLoop = true;
                if (normalIdleCoroutine == null)
                    normalIdleCoroutine = StartCoroutine(RandomLoopCoroutine(
                        LoopMode.NormalIdle,
                        NormalidleAnimationHashes,
                        NormalidleAnimations,
                        "一般待機"));
            }
            else
            {
                StopLoopCoroutine(ref normalIdleCoroutine);
            }
        }
    }

    // =========================================================
    // 共用隨機循環 Coroutine
    // =========================================================

    private IEnumerator RandomLoopCoroutine(
        LoopMode mode,
        int[] hashes,
        string[] names,
        string debugLabel)
    {
        while (IsLoopActive(mode))
        {
            int index = Random.Range(0, hashes.Length);
            Debug.Log($"[{debugLabel}] 播放動畫: {names[index]}");

            animator.CrossFadeInFixedTime(hashes[index], crossFadeDuration);
            yield return new WaitForSeconds(crossFadeDuration);
            yield return StartCoroutine(WaitForAnimationComplete(names[index]));
        }

        SetLoopCoroutineRef(mode, null);
    }

    // =========================================================
    // 單次播放
    // =========================================================

    /// <summary>
    /// 播放單次動畫（名稱），播放完畢後可選擇回到指定循環模式
    /// </summary>
    public void PlaySingleAnimation(
        string animationName,
        bool returnToLoop = false,
        Action onComplete = null,
        LoopMode returnLoopMode = LoopMode.NormalIdle)
    {
        StopAllLoops();

        if (singleAnimationCoroutine != null)
            StopCoroutine(singleAnimationCoroutine);

        singleAnimationCoroutine = StartCoroutine(
            PlaySingleAnimationCoroutine(animationName, returnToLoop, onComplete, returnLoopMode));
    }

    /// <summary>
    /// 播放單次動畫（Hash），播放完畢後可選擇回到指定循環模式
    /// </summary>
    public void PlaySingleAnimation(
        int animationHash,
        bool returnToLoop = false,
        Action onComplete = null,
        LoopMode returnLoopMode = LoopMode.NormalIdle)
    {
        StopAllLoops();

        if (singleAnimationCoroutine != null)
            StopCoroutine(singleAnimationCoroutine);

        singleAnimationCoroutine = StartCoroutine(
            PlaySingleAnimationCoroutineByHash(animationHash, returnToLoop, onComplete, returnLoopMode));
    }

    private IEnumerator PlaySingleAnimationCoroutine(
        string animationName, bool returnToLoop, Action onComplete, LoopMode returnLoopMode)
    {
        animator.CrossFadeInFixedTime(animationName, crossFadeDuration);
        yield return new WaitForSeconds(crossFadeDuration);
        yield return StartCoroutine(WaitForAnimationComplete(animationName));

        onComplete?.Invoke();

        if (returnToLoop)
            ApplyLoopMode(returnLoopMode);

        singleAnimationCoroutine = null;
    }

    private IEnumerator PlaySingleAnimationCoroutineByHash(
        int animationHash, bool returnToLoop, Action onComplete, LoopMode returnLoopMode)
    {
        animator.CrossFadeInFixedTime(animationHash, crossFadeDuration);
        yield return new WaitForSeconds(crossFadeDuration);
        yield return StartCoroutine(WaitForAnimationCompleteByHash(animationHash));

        onComplete?.Invoke();

        if (returnToLoop)
            ApplyLoopMode(returnLoopMode);

        singleAnimationCoroutine = null;
    }

    // =========================================================
    // 等待動畫完成
    // =========================================================

    private IEnumerator WaitForAnimationComplete(string animationName)
    {
        yield return null;

        while (true)
        {
            var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.IsName(animationName) && stateInfo.normalizedTime >= 0.95f)
                break;
            yield return null;
        }
    }

    private IEnumerator WaitForAnimationCompleteByHash(int animationHash)
    {
        yield return null;

        while (true)
        {
            var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.shortNameHash == animationHash && stateInfo.normalizedTime >= 0.95f)
                break;
            yield return null;
        }
    }

    // =========================================================
    // 工具方法
    // =========================================================

    /// <summary> 讀取對應循環旗標 </summary>
    private bool IsLoopActive(LoopMode mode)
    {
        return mode switch
        {
            LoopMode.SleepIdle => sleepIdleLoop,
            LoopMode.Talking => talkingLoop,
            LoopMode.NormalIdle => normalIdleLoop,
            _ => false
        };
    }

    /// <summary> 清除對應 Coroutine 參考 </summary>
    private void SetLoopCoroutineRef(LoopMode mode, Coroutine value)
    {
        switch (mode)
        {
            case LoopMode.SleepIdle: sleepIdleCoroutine = value; break;
            case LoopMode.Talking: talkingCoroutine = value; break;
            case LoopMode.NormalIdle: normalIdleCoroutine = value; break;
        }
    }

    /// <summary> 根據 LoopMode 啟動對應循環 </summary>
    private void ApplyLoopMode(LoopMode mode)
    {
        switch (mode)
        {
            case LoopMode.SleepIdle: SleepIdleLoop = true; break;
            case LoopMode.Talking: TalkingLoop = true; break;
            case LoopMode.NormalIdle: NormalIdleLoop = true; break;
        }
    }

    /// <summary> 停止所有循環 </summary>
    private void StopAllLoops()
    {
        sleepIdleLoop = false;
        talkingLoop = false;
        normalIdleLoop = false;

        StopLoopCoroutine(ref sleepIdleCoroutine);
        StopLoopCoroutine(ref talkingCoroutine);
        StopLoopCoroutine(ref normalIdleCoroutine);
    }

    private void StopLoopCoroutine(ref Coroutine coroutine)
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }
    }

    /// <summary> 立即停止所有動畫控制 </summary>
    public void StopAllAnimations()
    {
        StopAllLoops();

        if (singleAnimationCoroutine != null)
        {
            StopCoroutine(singleAnimationCoroutine);
            singleAnimationCoroutine = null;
        }
    }
}
