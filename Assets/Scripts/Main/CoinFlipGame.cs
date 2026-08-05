using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class CoinFlipGame : MonoBehaviour
{
    public static CoinFlipGame instance {  get; private set; }

    [Header("硬幣影片播放器與 CanvasGroup")]
    public VideoPlayer coinAVideoPlayer;  // 硬幣A的 VideoPlayer
    public VideoPlayer coinBVideoPlayer;  // 硬幣B的 VideoPlayer
    public VideoPlayer coinCVideoPlayer;  // 硬幣C的 VideoPlayer

    public CanvasGroup coinACanvasGroup;  // 硬幣A的 CanvasGroup
    public CanvasGroup coinBCanvasGroup;  // 硬幣B的 CanvasGroup
    public CanvasGroup coinCCanvasGroup;  // 硬幣C的 CanvasGroup

    [Header("硬幣 AudioSource")]
    public AudioSource coinAAudioSource;  // 硬幣A的 AudioSource
    public AudioSource coinBAudioSource;  // 硬幣B的 AudioSource
    public AudioSource coinCAudioSource;  // 硬幣C的 AudioSource

    [Header("影片素材")]
    public VideoClip headsClip;  // 正面影片（代表 1）
    public VideoClip[] tailsClip;  // 反面影片（代表 0）

    [Header("音效素材")]
    public AudioClip ClearClip;  // 正確的音效（代表 1）
    public AudioClip FailedClip;  // 失敗的音效（代表 0）

    [Header("遊戲設定")]
    [Range(0f, 1f)]
    public float displayInterval = 0.5f;  // 每個硬幣顯示的間隔時間
    public float fadeDuration = 0.5f;  // 淡入時間
    public float showDuration = 0.8f;  // 影片播放停留時間

    public Coroutine currentGame;

    public bool isFlipping = false;  // 是否正在擲硬幣中
    public int[] coinResults = new int[3];  // 儲存三個硬幣的結果



    void Start()
    {
        if(instance == null)
        {
            instance = this;
        }
        // 初始化畫面
        _init();
    }
    // 重置硬幣顯示（將 Alpha 歸 0）
    public void ResetCoins()
    {
        coinACanvasGroup.alpha = 0f;
        coinBCanvasGroup.alpha = 0f;
        coinCCanvasGroup.alpha = 0f;

        // 停止所有影片播放
        coinAVideoPlayer.Stop();
        coinBVideoPlayer.Stop();
        coinCVideoPlayer.Stop();

        coinAAudioSource.Stop();
        coinBAudioSource.Stop();
        coinCAudioSource.Stop();

        coinAVideoPlayer.targetTexture.Release();
        coinBVideoPlayer.targetTexture.Release();
        coinCVideoPlayer.targetTexture.Release();


    }

    // 顯示單個硬幣結果的影片，並用 CanvasGroup 淡入
    public IEnumerator ShowCoinResult(VideoPlayer player,AudioSource audio, CanvasGroup group, int result)
    {
        // 選擇對應的影片
        if (result == 1)
        {
            player.clip = headsClip;
            audio.clip = ClearClip;
        }
        else
        {
            player.clip = tailsClip[Random.Range(0, 2)];
            audio.clip = FailedClip;
        }

        // 播放影片
        player.Stop();
        player.Play();
        audio.Stop();
        audio.Play();

        // 淡入效果
        yield return StartCoroutine(FadeCanvasGroup(group, 0f, 1f, fadeDuration));

        // 保持顯示
        yield return new WaitForSeconds(showDuration);
    }

    // CanvasGroup 淡入淡出效果
    IEnumerator FadeCanvasGroup(CanvasGroup group, float from, float to, float duration)
    {
        float t = 0f;
        group.alpha = from;

        while (t < duration)
        {
            t += Time.deltaTime;
            float lerp = Mathf.Clamp01(t / duration);
            group.alpha = Mathf.Lerp(from, to, lerp);
            yield return null;
        }

        group.alpha = to;
    }

    public void _init()
    {
        if (currentGame != null)
        {
            StopCoroutine(currentGame);
            isFlipping = false;
        }
        ResetCoins();
    }
}
