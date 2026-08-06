using Unity.WebRTC;
using UnityEngine;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

/// <summary>
/// KIOSK WebRTC 管理器（v5：導入 DataChannel V2 協議）
///
/// 連線：
///   POST /streaming/createOffer  Body { sdp, type, accessToken }，帶 avatarBackendToken Cookie
///   回應為信封格式，processObject 是「字串包 JSON」，需拆兩層才拿得到 Answer SDP
///
/// DataChannel V2 協議重點（與舊版差異）：
///   chat 送出   : {"lang":"zh-TW","text":"...","v":"v2"}        ← 舊版直接送純文字
///   chat 接收   : {id, status, message:{type,content,tags}, msgenvent:{...}, speaking_status}
///                 舊版是平的 {status, text}，欄位位置全變了
///   command 送出: {"cmd":"...","arg":{...},"ts":秒,"v":"v2"}    ← 舊版 arg 是字串、v 是數字 1
///   command 回應: {"type":"ack","status":200}
///   echo 送出   : 純文字（直接 TTS，不經 LLM）
///
/// ⚠️ 執行緒注意：
///   onReceived 是從音訊執行緒 (OnAudioFilterRead) 呼叫的，
///   裡面不可以碰任何 Unity API（Time.realtimeSinceStartup 之類會直接丟例外，
///   而且例外會卡住音訊執行緒，連帶讓 ICE 斷線）。
///   統計資料只累加到欄位，實際輸出留到 Update() 在主執行緒做。
/// </summary>
public class KioskWebRTCManager : MonoBehaviour
{
    public static KioskWebRTCManager instance { get; private set; }

    private RTCPeerConnection peerConnection;
    private KioskAPIManager apiManager;
    public string sessionId;

    private RTCDataChannel chatChannel;
    private RTCDataChannel echoChannel;
    private RTCDataChannel commandChannel;

    private Dictionary<string, bool> channelStates = new Dictionary<string, bool>();

    // ===== 音訊 =====
    private AudioStreamTrack receivedAudioTrack;
    private List<float> audioBuffer = new List<float>();
    private int audioChannels = 2;
    private int audioSampleRate = 48000;
    private bool isRecordingAudio = false;

    public uLipSync.uLipSync lipSync;
    public AudioSource audioSource;

    [Header("V2 協議設定")]
    [Tooltip("協議版本，目前固定 v2")]
    public string protocolVersion = "v2";

    [Tooltip("送出 chat / command 時的預設語系")]
    public string defaultLanguage = "zh-TW";

    [Header("Offer 請求設定")]
    [Tooltip("勾選後會在 Offer Body 附上 persona / few_shot_examples（舊版格式）。\n" +
             "若新版人設改由後台設定，保持不勾選。")]
    public bool includePersona = false;

    [Tooltip("Offer Body 是否附上 audio_only。新版手冊未提及，預設不送。")]
    public bool includeAudioOnly = false;

    [Tooltip("是否在 SDP 加入 recvonly 的 video m-line。\n" +
             "KIOSK 網頁版是影音都收，後端可能預期 SDP 含 video。")]
    public bool includeVideoTransceiver = true;

    [Header("音訊除錯")]
    [Tooltip("連上後強制把 AudioSource 設成 2D、音量 1、不靜音")]
    public bool forceAudioSourceSettings = true;

    [Tooltip("每秒印一次收到的音訊量與音量")]
    public bool logAudioLevel = true;

    [Header("連線除錯")]
    [Tooltip("印出每一個 ICE candidate（很吵，排查用）")]
    public bool logIceCandidates = false;

    // ===== 對外事件 =====
    /// <summary>收到 chat / echo 的結構化訊息</summary>
    public event Action<ChatEvent> OnChatEvent;

    /// <summary>speaking_status 變化：idle / talking / finished</summary>
    public event Action<string> OnSpeakingStatusChanged;

    /// <summary>ICE 連線狀態變化</summary>
    public event Action<RTCIceConnectionState> OnIceStateChanged;

    private string lastSpeakingStatus;

    #region 資料結構

    [Serializable]
    public class AnswerResponse
    {
        public string sdp;
        public string type;
        public string session_id;
    }

    /// <summary>V2 chat / echo 事件</summary>
    public class ChatEvent
    {
        public string id;
        public string status;           // start | end
        public string type;             // text | card | map | qrcode | navigation | error
        public JToken content;          // 原始 content，型別依 type 而定
        public string[] tags;
        public string thinkingStatus;   // thinking | finished
        public string label;            // chat | echo
        public string msgType;          // chat | welcome | guide | RAG_Success | RAG_Fail
        public string speakingStatus;   // idle | talking | finished
        public string rawJson;

        /// <summary>type == text 時的文字內容；其他型別回傳 content 的字串形式</summary>
        public string Text
        {
            get
            {
                if (content == null) return "";
                if (content.Type == JTokenType.String) return content.ToString();
                return content["text"]?.ToString()
                    ?? content["content"]?.ToString()
                    ?? content.ToString();
            }
        }

        public bool IsText => type == "text";
        public bool IsError => type == "error";
    }

    // ===== 以下保留給既有程式相容（Server Main.cs 有 using static WebRTCManager） =====

    [Serializable]
    public class CommandData
    {
        public string cmd;
        public object arg;
        public long ts;
        public string v = "v2";
    }

    [Serializable] public class SkipArg { public string reason; }
    [Serializable] public class ResetArg { public string reason; }

    #endregion

    void Start()
    {
        if (instance == null) instance = this;

        StartCoroutine(WebRTC.Update());
        apiManager = GetComponent<KioskAPIManager>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (lipSync == null) lipSync = GetComponent<uLipSync.uLipSync>();
    }

    #region PeerConnection

    public void CreatePeerConnection(KioskAPIManager.TurnInformation turnInfo)
    {
        RTCConfiguration config = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer
                {
                    urls = turnInfo.urls,
                    username = turnInfo.username,
                    credential = turnInfo.credential
                }
            },
            iceTransportPolicy = RTCIceTransportPolicy.Relay
        };

        peerConnection = new RTCPeerConnection(ref config);

        peerConnection.OnIceCandidate = candidate =>
        {
            if (logIceCandidates)
                Debug.Log($"ICE Candidate: {candidate.Candidate}");
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            switch (state)
            {
                case RTCIceConnectionState.Connected:
                case RTCIceConnectionState.Completed:
                    Debug.Log($"🟢 ICE 連線狀態: {state}");
                    break;
                case RTCIceConnectionState.Disconnected:
                    Debug.LogWarning($"🟡 ICE 連線狀態: {state}（暫時中斷，可能會自行恢復）");
                    break;
                case RTCIceConnectionState.Failed:
                    Debug.LogError($"🔴 ICE 連線狀態: {state}（連線失敗）\n" +
                                   "   常見原因：TURN 憑證過期、網路中斷、" +
                                   "或音訊 callback 丟例外卡住執行緒");
                    break;
                default:
                    Debug.Log($"ICE 連線狀態: {state}");
                    break;
            }
            OnIceStateChanged?.Invoke(state);
        };

        peerConnection.OnIceGatheringStateChange = state =>
        {
            Debug.Log($"ICE Gathering State: {state}");
        };

        peerConnection.OnConnectionStateChange = state =>
        {
            Debug.Log($"📶 PeerConnection 狀態: {state}");
        };

        peerConnection.OnTrack = e =>
        {
            Debug.Log($"🟢 OnTrack 觸發！Kind={e.Track.Kind}, Type={e.Track.GetType().Name}");

            if (e.Track is AudioStreamTrack audioTrack)
            {
                Debug.Log($"🟢 收到音訊軌道！ID={audioTrack.Id}, ReadyState={audioTrack.ReadyState}");
                receivedAudioTrack = audioTrack;
                SetupAudioReceiver(audioTrack);
            }
            else if (e.Track is VideoStreamTrack)
            {
                // 有加 video transceiver 才會收到；本專案用不到，收下不處理
                Debug.Log("🎥 收到視訊軌道（本專案不使用，忽略）");
            }
        };

        peerConnection.OnDataChannel = channel =>
        {
            Debug.Log($"📡 收到後端建立的 DataChannel: {channel.Label}");
            SetupDataChannelEvents(channel, channel.Label);
        };
    }

    #endregion

    #region 音訊接收

    private void SetupAudioReceiver(AudioStreamTrack audioTrack)
    {
        audioTrack.onReceived += OnAudioDataReceived;
        Debug.Log("✅ 已設定音訊接收監聽器");

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>();

        if (audioSource == null)
        {
            Debug.LogError("❌ 找不到 AudioSource，音訊無法播放");
            return;
        }

        if (forceAudioSourceSettings)
        {
            audioSource.spatialBlend = 0f;   // 2D，避免因距離／方位聽不到
            audioSource.volume = 1f;
            audioSource.mute = false;
            audioSource.bypassEffects = false;
            audioSource.priority = 0;
        }

        audioSource.SetTrack(audioTrack);
        audioSource.loop = true;
        audioSource.Play();

        Debug.Log($"🔊 已開始播放音訊\n" +
                  $"   AudioSource 位於: {audioSource.gameObject.name}\n" +
                  $"   volume={audioSource.volume}, mute={audioSource.mute}, " +
                  $"spatialBlend={audioSource.spatialBlend}, isPlaying={audioSource.isPlaying}\n" +
                  $"   AudioListener: {(FindObjectOfType<AudioListener>() != null ? "✅ 有" : "❌ 沒有（沒有 Listener 就不會有聲音）")}\n" +
                  $"   全域音量={AudioListener.volume}, pause={AudioListener.pause}");

        if (lipSync == null)
            Debug.LogWarning("⚠️ lipSync 為 null，對嘴不會動作（不影響出聲）");
    }

    // 音訊執行緒與主執行緒共用的統計，務必上鎖
    private readonly object audioStatLock = new object();
    private int audioStatSamples;
    private float audioStatPeak;
    private int audioStatChannels;
    private int audioStatRate;
    private float audioStatNextLog;

    /// <summary>
    /// ⚠️ 由音訊執行緒呼叫，禁止使用任何 Unity API（Time / GameObject / Debug 以外）。
    /// </summary>
    private void OnAudioDataReceived(float[] data, int channels, int sampleRate)
    {
        audioChannels = channels;
        audioSampleRate = sampleRate;

        if (isRecordingAudio)
        {
            lock (audioBuffer) { audioBuffer.AddRange(data); }
        }

        if (lipSync != null)
            lipSync.OnDataReceived(data, channels);

        if (logAudioLevel)
        {
            float rms = CalculateRMS(data);
            lock (audioStatLock)
            {
                audioStatSamples += data.Length;
                if (rms > audioStatPeak) audioStatPeak = rms;
                audioStatChannels = channels;
                audioStatRate = sampleRate;
            }
        }
    }

    void Update()
    {
        if (!logAudioLevel) return;
        if (Time.realtimeSinceStartup < audioStatNextLog) return;
        audioStatNextLog = Time.realtimeSinceStartup + 1f;

        int samples; float peak; int ch; int rate;
        lock (audioStatLock)
        {
            samples = audioStatSamples;
            peak = audioStatPeak;
            ch = audioStatChannels;
            rate = audioStatRate;
            audioStatSamples = 0;
            audioStatPeak = 0f;
        }

        if (samples == 0) return;

        Debug.Log($"🎵 音訊接收中：{samples} samples/秒, {ch}ch @ {rate}Hz, " +
                  $"最大音量 RMS={peak:F4} " +
                  $"{(peak < 0.0001f ? "← 幾乎靜音，代表對方沒在說話" : "← 有聲音資料")}");
    }

    private float CalculateRMS(float[] data)
    {
        if (data == null || data.Length == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < data.Length; i++) sum += data[i] * data[i];
        return Mathf.Sqrt(sum / data.Length);
    }

    #endregion

    #region 建立並送出 Offer

    public IEnumerator CreateAndSendOffer()
    {
        var recvOnly = new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly };

        peerConnection.AddTransceiver(TrackKind.Audio, recvOnly);

        if (includeVideoTransceiver)
        {
            peerConnection.AddTransceiver(TrackKind.Video, new RTCRtpTransceiverInit
            {
                direction = RTCRtpTransceiverDirection.RecvOnly
            });
            Debug.Log("🎥 已加入 recvonly 的 video transceiver");
        }

        var dcInit = new RTCDataChannelInit { ordered = true };

        chatChannel = peerConnection.CreateDataChannel("chat", dcInit);
        echoChannel = peerConnection.CreateDataChannel("echo", dcInit);
        commandChannel = peerConnection.CreateDataChannel("command", dcInit);

        SetupDataChannelEvents(chatChannel, "chat");
        SetupDataChannelEvents(echoChannel, "echo");
        SetupDataChannelEvents(commandChannel, "command");

        Debug.Log("✅ 已建立 3 個 DataChannels: chat, echo, command");

        var options = new RTCOfferAnswerOptions { iceRestart = false };

        var offerOp = peerConnection.CreateOffer(ref options);
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError($"建立Offer失敗: {offerOp.Error.message}");
            yield break;
        }

        var offer = offerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref offer);
        yield return setLocalOp;

        if (setLocalOp.IsError)
        {
            Debug.LogError($"設定LocalDescription失敗: {setLocalOp.Error.message}");
            yield break;
        }

        Debug.Log("等待 ICE 收集完成...");
        while (peerConnection.GatheringState != RTCIceGatheringState.Complete)
            yield return null;
        Debug.Log("ICE 收集完成");

        yield return SendOfferToServer(peerConnection.LocalDescription);
    }

    private IEnumerator SendOfferToServer(RTCSessionDescription offer)
    {
        var body = new JObject
        {
            ["sdp"] = offer.sdp,
            ["type"] = "offer",
            ["accessToken"] = apiManager.accessToken
        };

        if (includeAudioOnly) body["audio_only"] = true;

        if (includePersona)
        {
            body["persona"] = new JObject
            {
                ["avatar_name"] = "Lumina",
                ["traits"] = new JArray("開朗", "幽默", "搞笑"),
                ["domain"] = "籤詩解讀、命理諮詢、心靈指引",
                ["role_title"] = "廟宇解籤師",
                ["avatar_id"] = "lumina"
            };
        }

        string jsonBody = body.ToString(Newtonsoft.Json.Formatting.None);

        var keys = new List<string>();
        foreach (var p in body) keys.Add(p.Key);
        var mLines = new List<string>();
        foreach (var line in offer.sdp.Split('\n'))
            if (line.StartsWith("m=")) mLines.Add(line.Trim());

        Debug.Log($"📤 發送 Offer 至 {apiManager.OFFER_ENDPOINT}\n" +
                  $"   欄位: {string.Join(", ", keys)}\n" +
                  $"   SDP m-line: {string.Join(" / ", mLines)}\n" +
                  $"   accessToken: {(string.IsNullOrEmpty(apiManager.accessToken) ? "❌ 空的！" : "有 " + apiManager.accessToken.Length + " 字元")}");

        var res = new KioskAPIManager.HttpResponse();
        yield return apiManager.PostJson(apiManager.OFFER_ENDPOINT, jsonBody, res);

        if (!res.IsSuccess)
        {
            Debug.LogError($"❌ Offer 請求失敗 [{res.status}]: {res.networkError}");
            Debug.LogError($"❌ 回應內容: {res.text}");
            yield break;
        }

        AnswerResponse response = ParseAnswer(res.text, out string code, out string msg);

        if (response == null || string.IsNullOrEmpty(response.sdp))
        {
            Debug.LogError($"❌ Answer 解析失敗（processResultCode={code}, msg={msg}）\n{res.text}");
            yield break;
        }

        Debug.Log($"✅ 取得 Answer（{code}） session_id={response.session_id}");
        sessionId = response.session_id;

        var answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = response.sdp };
        var setRemoteOp = peerConnection.SetRemoteDescription(ref answer);
        yield return setRemoteOp;

        if (setRemoteOp.IsError)
            Debug.LogError($"❌ SetRemoteDescription 失敗: {setRemoteOp.Error.message}");
        else
            Debug.Log($"🎉 WebRTC 連線建立成功！Session: {sessionId}");
    }

    /// <summary>
    /// 解析 createOffer 回應。相容三種格式：
    ///   A. processObject 為「字串包 JSON」（實際格式）
    ///   B. processObject 為物件
    ///   C. 直接就是 { sdp, type, session_id }
    /// </summary>
    private AnswerResponse ParseAnswer(string text, out string code, out string msg)
    {
        code = null;
        msg = null;
        if (string.IsNullOrEmpty(text)) return null;

        JObject root;
        try { root = JObject.Parse(text); }
        catch { return null; }

        code = root["processResultCode"]?.ToString();
        msg = root["processResultMsg"]?.ToString();

        JObject payload = null;
        var po = root["processObject"];

        if (po != null && po.Type == JTokenType.String)
        {
            try { payload = JObject.Parse(po.ToString()); } catch { }
        }
        else if (po is JObject poObj) payload = poObj;
        else if (root["sdp"] != null) payload = root;

        if (payload == null) return null;

        return new AnswerResponse
        {
            sdp = payload["sdp"]?.ToString(),
            type = payload["type"]?.ToString(),
            session_id = payload["session_id"]?.ToString()
        };
    }

    #endregion

    #region 送出訊息（V2）

    /// <summary>最底層：直接送出字串，不做任何包裝</summary>
    public void SendRaw(string message, string channelName)
    {
        RTCDataChannel channel = GetDataChannel(channelName);

        if (channel == null)
        {
            Debug.LogError($"❌ DataChannel [{channelName}] 不存在");
            return;
        }

        if (channel.ReadyState != RTCDataChannelState.Open)
        {
            Debug.LogWarning($"⚠️ DataChannel [{channelName}] 狀態: {channel.ReadyState}，無法發送訊息");
            return;
        }

        channel.Send(message);
        Debug.Log($"📤 [{channelName}] 已發送: {message}");
    }

    /// <summary>
    /// 相容舊呼叫方式。送往 chat 且內容不是 JSON 時，自動包成 V2 信封。
    /// （ElevenLabs_VAD 之類的舊程式碼可以不用改就正常運作）
    /// </summary>
    public void SendMessage(string message, string channelName = "chat")
    {
        if (channelName == "chat" && !LooksLikeJson(message))
        {
            SendChat(message);
            return;
        }
        SendRaw(message, channelName);
    }

    /// <summary>
    /// chat 頻道（V2）：{"lang":"zh-TW","text":"...","v":"v2"}
    /// 送使用者的話給 LLM，回覆走 OnChatEvent。
    /// </summary>
    public void SendChat(string text, string lang = null)
    {
        var o = new JObject
        {
            ["lang"] = string.IsNullOrEmpty(lang) ? defaultLanguage : lang,
            ["text"] = text,
            ["v"] = protocolVersion
        };
        SendRaw(o.ToString(Newtonsoft.Json.Formatting.None), "chat");
    }

    /// <summary>echo 頻道：純文字直接 TTS，不經過 LLM</summary>
    public void SendEcho(string text)
    {
        SendRaw(text, "echo");
    }

    /// <summary>
    /// command 頻道（V2）：{"cmd":"...","arg":{...},"ts":秒,"v":"v2"}
    /// 後端會在同一頻道回 {"type":"ack","status":200}
    /// </summary>
    public void SendCommand(string cmd, JObject arg = null)
    {
        var o = new JObject
        {
            ["cmd"] = cmd,
            ["arg"] = arg ?? new JObject(),
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["v"] = protocolVersion
        };
        SendRaw(o.ToString(Newtonsoft.Json.Formatting.None), "command");
    }

    /// <summary>問候。arg = { lang }</summary>
    public void SendSayHello(string lang = null)
    {
        SendCommand("say_hello", new JObject
        {
            ["lang"] = string.IsNullOrEmpty(lang) ? defaultLanguage : lang
        });
    }

    /// <summary>帶標籤的問候。tag 欄位名稱請與後端再確認一次</summary>
    public void SendSayHelloWithTag(string tag, string lang = null)
    {
        SendCommand("say_hello_with_tag", new JObject
        {
            ["lang"] = string.IsNullOrEmpty(lang) ? defaultLanguage : lang,
            ["tag"] = tag
        });
    }

    /// <summary>打斷目前這句話</summary>
    public void SendSkip(string reason = "user_interrupt")
    {
        SendCommand("skip", new JObject { ["reason"] = reason });
    }

    /// <summary>清空對話（原 Server Main 的 AvatarClearConversation）</summary>
    public void SendRes1(string reason = "conversation")
    {
        SendCommand("res_1", new JObject { ["reason"] = reason });
    }

    /// <summary>相容舊呼叫：SendJsonMessage(物件, "command")</summary>
    public void SendJsonMessage(object data, string channelName = "command")
    {
        string json;
        try { json = Newtonsoft.Json.JsonConvert.SerializeObject(data); }
        catch { json = JsonUtility.ToJson(data); }
        SendRaw(json, channelName);
    }

    public void SendBytes(byte[] data, string channelName = "chat")
    {
        RTCDataChannel channel = GetDataChannel(channelName);

        if (channel == null || channel.ReadyState != RTCDataChannelState.Open)
        {
            Debug.LogWarning($"⚠️ DataChannel [{channelName}] 未開啟");
            return;
        }

        channel.Send(data);
        Debug.Log($"📤 [{channelName}] 已發送二進位資料: {data.Length} bytes");
    }

    private static bool LooksLikeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        s = s.TrimStart();
        return s.StartsWith("{") || s.StartsWith("[");
    }

    #endregion

    #region 接收訊息（V2）

    private void SetupDataChannelEvents(RTCDataChannel channel, string channelName)
    {
        channelStates[channelName] = false;

        channel.OnOpen = () =>
        {
            Debug.Log($"📡 DataChannel [{channelName}] 已開啟");
            channelStates[channelName] = true;
        };

        channel.OnMessage = (bytes) =>
        {
            string message = Encoding.UTF8.GetString(bytes);
            Debug.Log($"📥 [{channelName}] 收到訊息: {message}");
            OnMessageReceived(channelName, message);
        };

        channel.OnClose = () =>
        {
            Debug.Log($"❌ DataChannel [{channelName}] 已關閉");
            channelStates[channelName] = false;
        };

        channel.OnError = (error) =>
        {
            Debug.LogError($"❌ DataChannel [{channelName}] 錯誤: {error}");
        };
    }

    private void OnMessageReceived(string channelName, string message)
    {
        switch (channelName)
        {
            case "chat":
            case "echo":
                HandleChatEnvelope(message, channelName);
                break;

            case "command":
                HandleCommandMessage(message);
                break;

            default:
                Debug.Log($"未知頻道 [{channelName}]: {message}");
                break;
        }
    }

    /// <summary>
    /// 解析 V2 的 chat / echo 事件信封。
    /// 舊版是平的 {status, text}，V2 改成 message.type / message.content。
    /// </summary>
    private void HandleChatEnvelope(string json, string channelName)
    {
        JObject root;
        try { root = JObject.Parse(json); }
        catch
        {
            Debug.LogWarning($"⚠️ [{channelName}] 不是 JSON，當純文字處理: {json}");
            return;
        }

        var msg = root["message"] as JObject;
        var env = root["msgenvent"] as JObject;

        var e = new ChatEvent
        {
            id = root["id"]?.ToString(),
            status = root["status"]?.ToString(),
            type = msg?["type"]?.ToString(),
            content = msg?["content"],
            tags = msg?["tags"]?.ToObject<string[]>(),
            thinkingStatus = env?["thinking_status"]?.ToString(),
            label = env?["label"]?.ToString() ?? channelName,
            msgType = env?["msg_type"]?.ToString(),
            speakingStatus = root["speaking_status"]?.ToString(),
            rawJson = json
        };

        Debug.Log($"💬 [{e.label}] status={e.status}, type={e.type}, " +
                  $"msg_type={e.msgType}, thinking={e.thinkingStatus}, speaking={e.speakingStatus}\n" +
                  $"   內容: {Shorten(e.Text, 120)}");

        // 只在 start 時把文字送進聊天畫面，避免 end 事件重複顯示
        if (e.status == "start" && e.IsText && !string.IsNullOrEmpty(e.Text))
        {
            if (ChatManager.instance != null)
                ChatManager.instance.AddAIMessage(e.Text);
        }

        if (e.IsError)
            Debug.LogError($"❌ 後端回報錯誤: {e.Text}");

        if (!string.IsNullOrEmpty(e.speakingStatus) && e.speakingStatus != lastSpeakingStatus)
        {
            lastSpeakingStatus = e.speakingStatus;
            OnSpeakingStatusChanged?.Invoke(e.speakingStatus);
        }

        OnChatEvent?.Invoke(e);
    }

    /// <summary>
    /// command 頻道回應。V2 統一為 {"type":"ack","status":200}
    /// </summary>
    private void HandleCommandMessage(string json)
    {
        JObject root;
        try { root = JObject.Parse(json); }
        catch
        {
            Debug.LogWarning($"⚠️ [command] 不是 JSON: {json}");
            return;
        }

        string type = root["type"]?.ToString();

        if (type == "ack")
        {
            int status = root["status"]?.ToObject<int>() ?? 0;
            if (status == 200)
                Debug.Log("✅ 指令已被後端接受 (ack 200)");
            else
                Debug.LogWarning($"⚠️ 指令回應非 200: {json}");
            return;
        }

        // 保留舊格式 {action, value}，本專案的錄音測試用
        string action = root["action"]?.ToString();
        if (!string.IsNullOrEmpty(action))
        {
            string value = root["value"]?.ToString();
            switch (action)
            {
                case "start_record": StartAudioRecording(); break;
                case "stop_record": StopAudioRecordingAndSave(value); break;
                default: Debug.Log($"未知命令: {action}"); break;
            }
            return;
        }

        Debug.Log($"⚙️ [command] 未辨識的訊息: {json}");
    }

    private static string Shorten(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    #endregion

    #region 頻道狀態

    private RTCDataChannel GetDataChannel(string channelName)
    {
        switch (channelName.ToLower())
        {
            case "chat": return chatChannel;
            case "echo": return echoChannel;
            case "command": return commandChannel;
            default: return null;
        }
    }

    public bool IsChannelOpen(string channelName)
    {
        return channelStates.ContainsKey(channelName) && channelStates[channelName];
    }

    /// <summary>等 command channel 開啟後送出 say_hello（對應手冊 Step 4）</summary>
    public IEnumerator WaitAndSayHello(string sttLanguage, float timeoutSec = 15f)
    {
        float elapsed = 0f;
        while (!IsChannelOpen("command"))
        {
            if (elapsed >= timeoutSec)
            {
                Debug.LogWarning("⚠️ 等待 command channel 開啟逾時，未發送 say_hello");
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        SendSayHello(sttLanguage);
        Debug.Log($"👋 已發送 say_hello（語系: {sttLanguage}）");
    }

    #endregion

    #region 錄音 / 音訊工具

    public void StartAudioRecording()
    {
        if (receivedAudioTrack == null)
        {
            Debug.LogWarning("⚠️ 尚未收到音訊軌道");
            return;
        }

        lock (audioBuffer) { audioBuffer.Clear(); }
        isRecordingAudio = true;
        Debug.Log("🔴 開始錄製音訊");
    }

    public void StopAudioRecordingAndSave(string fileName = "recorded_audio")
    {
        if (!isRecordingAudio)
        {
            Debug.LogWarning("⚠️ 目前沒有正在錄製");
            return;
        }

        isRecordingAudio = false;

        float[] samples;
        lock (audioBuffer) { samples = audioBuffer.ToArray(); }

        Debug.Log($"⏹️ 停止錄製，共 {samples.Length} samples");

        if (samples.Length > 0)
            SaveAudioAsWav(string.IsNullOrEmpty(fileName) ? "recorded_audio" : fileName,
                           samples, audioChannels, audioSampleRate);
        else
            Debug.LogWarning("⚠️ 沒有錄製到音訊資料");
    }

    private void SaveAudioAsWav(string fileName, float[] samples, int channels, int sampleRate)
    {
        string filePath = Path.Combine(Application.persistentDataPath, fileName + ".wav");

        try
        {
            File.WriteAllBytes(filePath, ConvertToWav(samples, channels, sampleRate));
            Debug.Log($"✅ 音訊已保存: {filePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ 保存音訊失敗: {e.Message}");
        }
    }

    private byte[] ConvertToWav(float[] samples, int channels, int sampleRate)
    {
        int sampleCount = samples.Length;
        int byteRate = sampleRate * channels * 2;

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + sampleCount * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((ushort)(channels * 2));
            writer.Write((ushort)16);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleCount * 2);

            foreach (float sample in samples)
                writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));

            return stream.ToArray();
        }
    }

    public float[] GetAudioSpectrum(int spectrumSize = 256)
    {
        if (audioSource == null) return null;
        float[] spectrum = new float[spectrumSize];
        audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
        return spectrum;
    }

    public void LogAudioStatus()
    {
        Debug.Log($"🔊 音訊狀態\n" +
                  $"   收到音訊軌道: {(receivedAudioTrack != null ? "✅ 有" : "❌ 沒有")}\n" +
                  $"   AudioSource : {(audioSource == null ? "❌ 沒有" : audioSource.gameObject.name)}\n" +
                  $"   isPlaying   : {(audioSource != null && audioSource.isPlaying ? "✅ 播放中" : "❌ 沒在播")}\n" +
                  $"   volume      : {(audioSource != null ? audioSource.volume.ToString() : "-")}\n" +
                  $"   mute        : {(audioSource != null ? audioSource.mute.ToString() : "-")}\n" +
                  $"   spatialBlend: {(audioSource != null ? audioSource.spatialBlend.ToString() : "-")}\n" +
                  $"   AudioListener: {(FindObjectOfType<AudioListener>() != null ? "✅ 有" : "❌ 沒有")}\n" +
                  $"   全域音量     : {AudioListener.volume}（pause={AudioListener.pause}）\n" +
                  $"   uLipSync     : {(lipSync != null ? "✅ 有" : "❌ 沒有")}");
    }

    #endregion

    void OnDestroy()
    {
        if (receivedAudioTrack != null)
            receivedAudioTrack.onReceived -= OnAudioDataReceived;

        if (chatChannel != null) { chatChannel.Close(); chatChannel.Dispose(); }
        if (echoChannel != null) { echoChannel.Close(); echoChannel.Dispose(); }
        if (commandChannel != null) { commandChannel.Close(); commandChannel.Dispose(); }

        peerConnection?.Dispose();
    }
}
