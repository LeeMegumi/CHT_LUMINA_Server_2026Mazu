using System.Collections;
using UnityEngine;

/// <summary>
/// KIOSK 新版連線流程入口（由原 AvatarController 修改）
/// 流程對應「KIOSK 連線流程.md」：
///   1. POST /auth/kiosk      驗證 Kiosk Code
///   2. POST /streaming/start 建立 Streaming Session
///   3. POST /webrtc/turn     取得 TURN + access_token
///   4. 建立 PeerConnection → POST /webrtc/offer → RTC Connected
///   5. 初始化（背景 / 語系由 streamingSettings 提供）
///   6. sendCommandMessage("say_hello") → 開始聊天
/// </summary>
public class KioskAvatarController : MonoBehaviour
{
    private KioskAPIManager apiManager;
    private KioskWebRTCManager webrtcManager;
    private KioskUIController uiController;

    void Start()
    {
        // ⚠️ 改為「先找既有的，找不到才新增」
        // 若你已在 Inspector 手動掛上 KioskAPIManager 並填好 BASE_URL / companyId，
        // 原本的 AddComponent 會再多建一個空白的，導致設定值全部失效。
        apiManager = GetComponent<KioskAPIManager>() ?? gameObject.AddComponent<KioskAPIManager>();
        webrtcManager = GetComponent<KioskWebRTCManager>() ?? gameObject.AddComponent<KioskWebRTCManager>();
        uiController = GetComponent<KioskUIController>() ?? gameObject.AddComponent<KioskUIController>();

        // 開始連線流程
        // 注意：quickLoginCode 為浮動代碼（後台產生），請在連線前先設定
        // 例如由 UI 輸入後呼叫 StartConnect(code)
        StartCoroutine(ConnectToAvatar());
    }

    /// <summary>
    /// 若 Kiosk Code 由 UI 輸入，可改用此方法啟動
    /// </summary>
    public void StartConnect(string kioskCode)
    {
        apiManager.quickLoginCode = kioskCode;
        StartCoroutine(ConnectToAvatar());
    }

    IEnumerator ConnectToAvatar()
    {
        // Step 1: 驗證 Kiosk Code（取得 HttpOnly Cookie）
        yield return apiManager.AuthKiosk();
        if (!apiManager.isAuthSuccess)
        {
            // 錯誤碼參考：NoScopeStreamingFound(代碼錯誤) /
            //            NoCertifiableStreamingFound(連線滿額) / ERR_AUTH_999
            Debug.LogError($"連線中止：Kiosk 驗證失敗 ({apiManager.lastErrorCode}) {apiManager.lastErrorMessage}");
            yield break;
        }

        // Step 2: 建立 Streaming Session（取得背景 / Logo / 語系設定）
        yield return apiManager.StartStreaming();
        if (!apiManager.isStreamingStarted)
        {
            Debug.LogError($"連線中止：Streaming 啟動失敗 ({apiManager.lastErrorCode})");
            yield break;
        }

        // Step 3: 取得 TURN 資訊與 access_token
        yield return apiManager.GetTurnInformation();
        if (!apiManager.isTurnReady)
        {
            Debug.LogError("連線中止：取得 TURN 資訊失敗");
            yield break;
        }

        // Step 4: 建立 PeerConnection 並送出 Offer（/webrtc/offer）
        webrtcManager.CreatePeerConnection(apiManager.turnInfo);
        yield return webrtcManager.CreateAndSendOffer();

        // Step 5: 依 streamingSettings 初始化畫面（背景 / Logo / 語系）
        // TODO: 依專案需求套用 apiManager.streamingSettings
        //   backgroundtype / backgroundColorCode / avatarLogoFileUrl / avatarDefaultLanguage

        // Step 6: command channel 開啟後發送 say_hello
        string lang = apiManager.streamingSettings != null
            ? apiManager.streamingSettings.avatarDefaultLanguage
            : "zh-TW";
        yield return webrtcManager.WaitAndSayHello(lang);

        Debug.Log("Avatar連線完成！");
    }
}
