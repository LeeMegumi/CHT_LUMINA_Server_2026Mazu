using UnityEngine;
using UnityEngine.UI;

public class CountdownBarController : MonoBehaviour
{
    [Header("設定倒數時間 (秒)")]
    public float totalTime = 300f; // 300 秒

    [Header("連結倒數用 Image")]
    public Image countdownImage;

    public float remainingTime;
    public bool isCounting = false;

    void Start()
    {
        // 初始化
        remainingTime = totalTime;
        if (countdownImage != null)
        {
            // 確保 Image 是 Filled + Horizontal
            countdownImage.type = Image.Type.Filled;
            countdownImage.fillMethod = Image.FillMethod.Vertical;
            countdownImage.fillOrigin = (int)Image.OriginVertical.Bottom; // 從右邊被裁掉
            countdownImage.fillAmount = 1f; // 一開始滿格
        }

        // 一開始就開始倒數（如果你想之後再啟動，可以把這行拿掉改用 StartCountdown）
    }

    void Update()
    {
        if (!isCounting || countdownImage == null)
            return;

        if (remainingTime > 0f)
        {
            // 扣時間
            remainingTime -= Time.deltaTime;
            if (remainingTime < 0f)
                remainingTime = 0f;

            // 計算 0~1 比例
            float ratio = remainingTime / totalTime;
            countdownImage.fillAmount = ratio; // 控制長條長度[web:13][web:17][web:20]
        }
        else
        {
            // 歸零後關閉倒數（可依需求觸發事件）
            isCounting = false;
            // TODO: 時間到時要做的事
        }
    }

    // 如果之後想重置並重新開始倒數，可以呼叫這個方法
    public void ResetAndStart()
    {
        remainingTime = totalTime;
        if (countdownImage != null)
            countdownImage.fillAmount = 1f;
        isCounting = true;
    }

    // 外部可以暫停/繼續
    public void Pause()
    {
        isCounting = false;
    }

    public void Resume()
    {
        if (remainingTime > 0f)
            isCounting = true;
    }
    public void ResetAndPause()
    {
        remainingTime = totalTime;
        isCounting = false;
    }
}
