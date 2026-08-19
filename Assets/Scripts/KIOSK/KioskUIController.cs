using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// KIOSK 版 UI 控制器（由原 UI Controller.cs 修改）
///
/// 與舊版差異：
///   1. 目標改為 KioskWebRTCManager（舊版是 WebRTCManager）
///   2. 原本被註解掉的按鍵測試功能改為可用，且訊息內容改到 Inspector 上設定，
///      不用改程式碼就能換測試句子
///   3. 多了狀態檢查鍵，可即時看三個 DataChannel 開了沒
///
/// 注意：這是新檔案，原本的 UI Controller.cs 完全沒動，
/// 舊的 AvatarController 流程仍可正常使用。
/// </summary>
public class KioskUIController : MonoBehaviour
{
    [System.Serializable]
    public class KeyBinding
    {
        [Tooltip("觸發按鍵")]
        public KeyCode key = KeyCode.A;

        [Tooltip("要送出的訊息內容")]
        [TextArea(1, 3)]
        public string message = "";

        [Tooltip("送往哪個 DataChannel：chat / echo / command")]
        public string channel = "chat";

        [Tooltip("備註，只顯示在 Inspector，不影響功能")]
        public string note = "";
    }

    [Header("目標")]
    [Tooltip("留空會自動從同一個 GameObject 上抓")]
    [SerializeField] public KioskWebRTCManager webRTCManager;

    [Header("按鍵測試")]
    [Tooltip("是否啟用按鍵測試（正式展示時建議關閉，避免誤觸）")]
    [SerializeField] public bool enableKeyboardTest = true;

    [SerializeField]
    public List<KeyBinding> bindings = new List<KeyBinding>
    {
        new KeyBinding { key = KeyCode.Alpha0, message = "你好",                         channel = "chat",  note = "打招呼" },
        new KeyBinding { key = KeyCode.Alpha1, message = "你叫什麼名字？",                channel = "chat",  note = "測試回覆" },
        new KeyBinding { key = KeyCode.Alpha2, message = "我抽到第二十五籤，可以幫我唸出籤詩並解籤嗎？",  channel = "chat",  note = "解籤情境" },
        new KeyBinding { key = KeyCode.Alpha3, message = "How old are you?",             channel = "chat",  note = "英文測試" },
        new KeyBinding { key = KeyCode.Alpha4, message = "測試語音直接播放",              channel = "echo",  note = "echo：不經 LLM 直接 TTS" },
    };

    [Header("功能鍵")]
    [Tooltip("印出三個 DataChannel 目前的開啟狀態")]
    [SerializeField] public KeyCode statusKey = KeyCode.F1;

    [Tooltip("重新送出 say_hello 問候")]
    [SerializeField] public KeyCode sayHelloKey = KeyCode.F2;

    [Tooltip("印出音訊播放狀態（AudioSource / AudioListener / 音軌）")]
    [SerializeField] public KeyCode audioStatusKey = KeyCode.F3;

    [Tooltip("送出 skip 指令，打斷目前這句話")]
    [SerializeField] public KeyCode skipKey = KeyCode.F4;

    [Tooltip("say_hello 使用的語系")]
    [SerializeField] public string sttLanguage = "zh-TW";

    void Start()
    {
        if (webRTCManager == null)
            webRTCManager = GetComponent<KioskWebRTCManager>();

        if (webRTCManager == null)
            Debug.LogWarning("⚠️ KioskUIController 找不到 KioskWebRTCManager，按鍵測試不會有作用");
        else if (enableKeyboardTest)
            LogHelp();
    }

    private void LogHelp()
    {
        var sb = new System.Text.StringBuilder("⌨️ 按鍵測試已啟用：\n");
        foreach (var b in bindings)
        {
            if (string.IsNullOrEmpty(b.message)) continue;
            sb.AppendLine($"   [{b.key}] → ({b.channel}) {b.message}" +
                          (string.IsNullOrEmpty(b.note) ? "" : $"　// {b.note}"));
        }
        sb.AppendLine($"   [{statusKey}] → 查看 DataChannel 狀態");
        sb.AppendLine($"   [{sayHelloKey}] → 重送 say_hello");
        sb.AppendLine($"   [{audioStatusKey}] → 查看音訊播放狀態");
        sb.AppendLine($"   [{skipKey}] → 送出 skip（打斷目前這句）");
        Debug.Log(sb.ToString());
    }

    void Update()
    {
        if (!enableKeyboardTest || webRTCManager == null) return;

        foreach (var b in bindings)
        {
            if (b == null || string.IsNullOrEmpty(b.message)) continue;
            if (Input.GetKeyUp(b.key))
                Send(b.message, string.IsNullOrEmpty(b.channel) ? "chat" : b.channel);
        }

        if (Input.GetKeyUp(statusKey)) LogChannelStatus();
        if (Input.GetKeyUp(sayHelloKey)) webRTCManager.SendSayHello(sttLanguage);
        if (Input.GetKeyUp(audioStatusKey)) webRTCManager.LogAudioStatus();
        if (Input.GetKeyUp(skipKey)) webRTCManager.SendSkip();
    }

    /// <summary>送出訊息（也可以從 UI Button 的 OnClick 直接呼叫）</summary>
    public void Send(string message, string channel = "chat")
    {
        if (webRTCManager == null)
        {
            Debug.LogWarning("⚠️ 尚未連線，無法送出訊息");
            return;
        }

        if (!webRTCManager.IsChannelOpen(channel))
        {
            Debug.LogWarning($"⚠️ DataChannel [{channel}] 尚未開啟，訊息未送出：{message}");
            return;
        }

        // chat 走 V2 信封，echo 是純文字直接 TTS
        switch (channel)
        {
            case "chat":
                webRTCManager.SendChat(message, sttLanguage);
                break;
            case "echo":
                webRTCManager.SendEcho(message);
                break;
            default:
                webRTCManager.SendMessage(message, channel);
                break;
        }
    }

    /// <summary>給 UI Button 用的單參數版本（Unity 的 OnClick 只吃一個參數）</summary>
    public void SendChatMessage(string message) => Send(message, "chat");

    public void LogChannelStatus()
    {
        if (webRTCManager == null) return;

        string Mark(string ch) => webRTCManager.IsChannelOpen(ch) ? "🟢 開啟" : "🔴 未開";

        Debug.Log($"📡 DataChannel 狀態\n" +
                  $"   chat    : {Mark("chat")}\n" +
                  $"   echo    : {Mark("echo")}\n" +
                  $"   command : {Mark("command")}\n" +
                  $"   Session : {(string.IsNullOrEmpty(webRTCManager.sessionId) ? "(尚未取得)" : webRTCManager.sessionId)}");
    }
}
