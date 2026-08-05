using Unity.WebRTC;
using UnityEngine;
using System.Linq;
using System.Text;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using static AvatarAPIManager;
using Newtonsoft.Json.Linq;

public class WebRTCManager : MonoBehaviour
{
    public static WebRTCManager instance {  get; private set; }
    private RTCPeerConnection peerConnection;
    private AvatarAPIManager apiManager;
    public string sessionId;

    private RTCDataChannel chatChannel;
    private RTCDataChannel echoChannel;
    private RTCDataChannel commandChannel;

    private Dictionary<string, bool> channelStates = new Dictionary<string, bool>();

    // ✅ 新增：音訊相關

    private AudioStreamTrack receivedAudioTrack;
    private List<float> audioBuffer = new List<float>();
    private int audioChannels = 2;
    private int audioSampleRate = 48000;
    private bool isRecordingAudio = false;

    public uLipSync.uLipSync lipSync;
    private AudioSource audioSource;

    [System.Serializable]
    public class AnswerResponse
    {
        public string sdp;
        public string type;
        public string session_id;
    }

    [System.Serializable]
    public class CompleteOfferRequest
    {
        public string sdp;
        public string type;
        public bool audio_only;
        public Persona persona;
        public FewShotExample[] few_shot_examples;
    }

    [System.Serializable]
    public class Persona
    {
        public string avatar_name;
        public string[] traits;
        public string domain;
        public string role_title;
        public string avatar_id;
    }

    [System.Serializable]
    public class FewShotExample
    {
        public string role;
        public string content;
    }
    void Start()
    {
        if (instance == null)
        {
            instance = this;
        }
        StartCoroutine(WebRTC.Update());
        apiManager = GetComponent<AvatarAPIManager>();
        audioSource = GetComponent<AudioSource>();
        lipSync = GetComponent<uLipSync.uLipSync>();
    }

    public void CreatePeerConnection(AvatarAPIManager.TurnInformation turnInfo)
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
            Debug.Log($"ICE Candidate: {candidate.Candidate}");
        };

        peerConnection.OnIceConnectionChange = state =>
        {
            Debug.Log($"ICE Connection State: {state}");
        };

        peerConnection.OnIceGatheringStateChange = state =>
        {
            Debug.Log($"ICE Gathering State: {state}");
        };

        // ✅ 修改：處理接收到的音視訊軌道
        peerConnection.OnTrack = e =>
        {
            Debug.Log($"🟢🟢🟢 OnTrack 觸發！Kind={e.Track.Kind}, Type={e.Track.GetType().Name}");  // ✅ 詳細資訊

            /*if (e.Track is VideoStreamTrack videoTrack)
            {
                Debug.Log("收到視訊軌道");
                // 你可以將視訊顯示在 RawImage 上
                // GetComponent<AvatarController>().DisplayVideo(videoTrack);
            }
            else */if (e.Track is AudioStreamTrack audioTrack)
            {
                Debug.Log($"🟢🟢🟢 收到音訊軌道！ID={audioTrack.Id}, ReadyState={audioTrack.ReadyState}");
                receivedAudioTrack = audioTrack;
                SetupAudioReceiver(audioTrack);
            }
        };

        peerConnection.OnDataChannel = channel =>
        {
            Debug.Log($"📡 收到後端建立的 DataChannel: {channel.Label}");
            SetupDataChannelEvents(channel, channel.Label);
        };
    }

    // ✅ 新增：設定音訊接收
    private void SetupAudioReceiver(AudioStreamTrack audioTrack)
    {
        // 訂閱音訊資料接收事件
        audioTrack.onReceived += OnAudioDataReceived;
        Debug.Log("✅ 已設定音訊接收監聽器");
        // 如果要播放音訊，可以綁定到 AudioSource
        if (audioSource != null)
        {
            audioSource.SetTrack(audioTrack);
            audioSource.loop = true;
            audioSource.Play();
            Debug.Log("🔊 已開始播放音訊");

        }
       
    }

    // ✅ 新增：音訊資料接收回調
    private void OnAudioDataReceived(float[] data, int channels, int sampleRate)
    {
        // 更新音訊參數
        audioChannels = channels;
        audioSampleRate = sampleRate;
        // 即時處理音訊資料
        //Debug.Log($"🎵 收到音訊資料: {data.Length} samples, {channels} channels, {sampleRate} Hz");

        // 如果正在錄製，將資料加入 buffer
        if (isRecordingAudio)
        {
            audioBuffer.AddRange(data);
        }
        lipSync.OnDataReceived(data, channels);
        // 你可以在這裡做其他處理，例如：
        // 1. 音訊分析（頻譜、音量等）
        // 2. 即時音訊效果處理
        // 3. 語音辨識

        // 計算音量 (RMS)
        float rms = CalculateRMS(data);
        if (rms > 0.01f) // 有聲音時才顯示
        {
            //Debug.Log($"📊 音量 RMS: {rms:F4}");
        }
        
    }

    

    // ✅ 新增：計算 RMS 音量
    private float CalculateRMS(float[] data)
    {
        float sum = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i] * data[i];
        }
        return Mathf.Sqrt(sum / data.Length);
    }

    // ✅ 新增：開始錄製音訊
    public void StartAudioRecording()
    {
        if (receivedAudioTrack == null)
        {
            Debug.LogWarning("⚠️ 尚未收到音訊軌道");
            return;
        }

        audioBuffer.Clear();
        isRecordingAudio = true;
        Debug.Log("🔴 開始錄製音訊");
    }

    // ✅ 新增：停止錄製並保存為 WAV
    public void StopAudioRecordingAndSave(string fileName = "recorded_audio")
    {
        if (!isRecordingAudio)
        {
            Debug.LogWarning("⚠️ 目前沒有正在錄製");
            return;
        }

        isRecordingAudio = false;
        Debug.Log($"⏹️ 停止錄製，共 {audioBuffer.Count} samples");

        if (audioBuffer.Count > 0)
        {
            SaveAudioAsWav(fileName, audioBuffer.ToArray(), audioChannels, audioSampleRate);
        }
        else
        {
            Debug.LogWarning("⚠️ 沒有錄製到音訊資料");
        }
    }

    // ✅ 新增：保存音訊為 WAV 檔案
    private void SaveAudioAsWav(string fileName, float[] samples, int channels, int sampleRate)
    {
        string filePath = Path.Combine(Application.persistentDataPath, fileName + ".wav");

        try
        {
            // 將 float[] 轉換為 WAV 格式的 byte[]
            byte[] wavData = ConvertToWav(samples, channels, sampleRate);

            // 寫入檔案
            File.WriteAllBytes(filePath, wavData);

            Debug.Log($"✅ 音訊已保存: {filePath}");
            Debug.Log($"📁 檔案大小: {wavData.Length / 1024f:F2} KB");
            Debug.Log($"⏱️ 時長: {samples.Length / channels / (float)sampleRate:F2} 秒");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ 保存音訊失敗: {e.Message}");
        }
    }

    // ✅ 新增：將 float[] 音訊資料轉換為 WAV 格式
    private byte[] ConvertToWav(float[] samples, int channels, int sampleRate)
    {
        int sampleCount = samples.Length;
        int byteRate = sampleRate * channels * 2; // 16-bit

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            // WAV Header
            // RIFF chunk
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + sampleCount * 2); // File size - 8
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // fmt chunk size
            writer.Write((ushort)1); // Audio format (1 = PCM)
            writer.Write((ushort)channels); // Channel count
            writer.Write(sampleRate); // Sample rate
            writer.Write(byteRate); // Byte rate
            writer.Write((ushort)(channels * 2)); // Block align
            writer.Write((ushort)16); // Bits per sample

            // data chunk
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(sampleCount * 2); // Data size

            // 寫入音訊資料 (float 轉 16-bit PCM)
            foreach (float sample in samples)
            {
                short pcmSample = (short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
                writer.Write(pcmSample);
            }

            return stream.ToArray();
        }
    }

    // ✅ 新增：取得即時音訊資料的方法（不保存）
    public float[] GetRealtimeAudioData(int sampleCount = 1024)
    {
        if (audioBuffer.Count < sampleCount)
        {
            return null;
        }

        // 取得最新的 N 個樣本
        int startIndex = Mathf.Max(0, audioBuffer.Count - sampleCount);
        return audioBuffer.GetRange(startIndex, sampleCount).ToArray();
    }

    // ✅ 新增：取得音訊頻譜資料（用於視覺化）
    public float[] GetAudioSpectrum(int spectrumSize = 256)
    {
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource == null) return null;

        float[] spectrum = new float[spectrumSize];
        audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
        return spectrum;
    }

    public IEnumerator CreateAndSendOffer()
    {
        RTCRtpTransceiverInit transceiverInit = new RTCRtpTransceiverInit
        {
            direction = RTCRtpTransceiverDirection.RecvOnly
        };

        AudioStreamTrack audioTrack = new AudioStreamTrack();
        //VideoStreamTrack videoTrack = new VideoStreamTrack(GetComponent<AvatarController>().videoRT);
        //peerConnection.AddTransceiver(videoTrack, transceiverInit);
        peerConnection.AddTransceiver(TrackKind.Audio, transceiverInit);

        RTCDataChannelInit dcInit = new RTCDataChannelInit
        {
            ordered = true
        };

        chatChannel = peerConnection.CreateDataChannel("chat", dcInit);
        echoChannel = peerConnection.CreateDataChannel("echo", dcInit);
        commandChannel = peerConnection.CreateDataChannel("command", dcInit);

        SetupDataChannelEvents(chatChannel, "chat");
        SetupDataChannelEvents(echoChannel, "echo");
        SetupDataChannelEvents(commandChannel, "command");

        Debug.Log("✅ 已建立 3 個 DataChannels: chat, echo, command");

        RTCOfferAnswerOptions options = new RTCOfferAnswerOptions
        {
            iceRestart = false,
            /*offerToReceiveAudio = true,
            offerToReceiveVideo = false*/
        };

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
        {
            yield return null;
        }
        Debug.Log("ICE 收集完成");

        var completeOffer = peerConnection.LocalDescription;
        yield return SendOfferToServer(completeOffer, apiManager.accessToken, apiManager.OFFER_URL);
    }

    // [保留原有的 DataChannel 相關方法...]
    private void SetupDataChannelEvents(RTCDataChannel channel, string channelName)
    {
        channelStates[channelName] = false;

        channel.OnOpen = () =>
        {
            Debug.Log($"📡 DataChannel [{channelName}] 已開啟");
            channelStates[channelName] = true;

            if (channelName == "chat")
            {
                //SendMessage("HI", "chat");
            }
        };

        channel.OnMessage = (bytes) =>
        {
            string message = System.Text.Encoding.UTF8.GetString(bytes);
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

    public void SendMessage(string message, string channelName = "chat")
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

    public void SendJsonMessage(object data, string channelName = "command")
    {
        string jsonMessage = JsonUtility.ToJson(data);
        SendMessage(jsonMessage, channelName);
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

    private RTCDataChannel GetDataChannel(string channelName)
    {
        switch (channelName.ToLower())
        {
            case "chat":
                return chatChannel;
            case "echo":
                return echoChannel;
            case "command":
                return commandChannel;
            default:
                return null;
        }
    }

    public bool IsChannelOpen(string channelName)
    {
        return channelStates.ContainsKey(channelName) && channelStates[channelName];
    }

    private void OnMessageReceived(string channelName, string message)
    {
        switch (channelName)
        {
            case "chat":
                Debug.Log($"💬 聊天訊息: {message}");
                if (ExtractTextFromResponse("status", message) == "start")
                {
                    ChatManager.instance.AddAIMessage(ExtractTextFromResponse("text", message));
                }
                
                if (ServerMain.instance.QACount == 0)
                {
                    
                }
                break;

            case "echo":
                Debug.Log($"🔊 Echo: {message}");
                SendMessage(message, "echo");
                break;

            case "command":
                Debug.Log($"⚙️ 命令: {message}");
                HandleCommand(message);
                break;
        }
    }
    public string ExtractTextFromResponse(string part,string jsonString)
    {
        JObject jsonObject = JObject.Parse(jsonString);
        return jsonObject[part].ToString();
    }

    private void HandleCommand(string message)
    {
        try
        {
            var command = JsonUtility.FromJson<CommandMessage>(message);
            Debug.Log($"執行命令: {command.action} = {command.value}");

            switch (command.action)
            {
                case "play":
                    Debug.Log("播放動畫");
                    break;
                case "stop":
                    Debug.Log("停止動畫");
                    break;
                case "start_record":
                    StartAudioRecording();
                    break;
                case "stop_record":
                    StopAudioRecordingAndSave(command.value);
                    break;
                default:
                    Debug.Log($"未知命令: {command.action}");
                    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"無法解析命令: {e.Message}");
        }
    }

    [System.Serializable]
    public class CommandMessage
    {
        public string action;
        public string value;
    }

    [System.Serializable]
    public class CommandData
    {
        public string cmd;           // 指令名稱
        public object arg;           // 指令參數（可為 null）
        public long ts;              // 時間戳
        public int v = 1;            // 協議版本號（預設為 1）
    }
    [System.Serializable]
    public class SkipArg
    {
        public string reason;
    }

    // Reset 指令的參數
    [System.Serializable]
    public class ResetArg
    {
        public string reason;
    }

    private IEnumerator SendOfferToServer(RTCSessionDescription offer, string accessToken, string offerUrl)
    {
        // 建立完整的 Offer 請求（包含 Persona 和 Few-shot）
        var completeOfferData = new CompleteOfferRequest
        {
            sdp = offer.sdp,  // 
            type = "offer",
            audio_only= true,
            persona = new Persona
            {
                avatar_name = "Lumina",
                traits = new string[] { "開朗", "幽默", "搞笑" },
                domain = "籤詩解讀、命理諮詢、心靈指引",
                role_title = "廟宇解籤師",
                avatar_id = "lumina"
            },
            few_shot_examples = new FewShotExample[]
            {
            new FewShotExample
            {
                role = "user",
                content = "你好"
            },
            new FewShotExample
            {
                role = "assistant",
                content = "Lumi~ 客人你好呀！人工智慧通玄理，命運未來啟鴻圖，歡迎來到抽籤未來，您今天是來求籤問事的嗎？"
            },
            new FewShotExample
            {
                role = "user",
                content = "我抽到籤了，可以幫我解籤嗎？"
            },
            new FewShotExample
            {
                role = "assistant",
                content = "Lumi~ 當然可以！看我的，領域展開!!靈籤詩萬解!!"
            }
            }
        };

        // 使用 JsonUtility 序列化（如果需要更複雜的序列化可以改用 Newtonsoft.Json）
        string jsonBody = JsonUtility.ToJson(completeOfferData, true);
        Debug.Log($"📤 發送的完整 JSON:\n{jsonBody}");

        UnityWebRequest request = new UnityWebRequest(offerUrl, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {accessToken}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"❌ 請求失敗: {request.error}");
            Debug.LogError($"❌ 回應內容: {request.downloadHandler.text}");
        }
        else
        {
            Debug.Log($"✅ 伺服器回應: {request.downloadHandler.text}");

            AnswerResponse response = JsonUtility.FromJson<AnswerResponse>(request.downloadHandler.text);
            sessionId = response.session_id;

            RTCSessionDescription answer = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = response.sdp
            };

            var setRemoteOp = peerConnection.SetRemoteDescription(ref answer);
            yield return setRemoteOp;

            if (!setRemoteOp.IsError)
            {
                Debug.Log($"🎉 WebRTC 連線建立成功！Session: {response.session_id}");
            }
        }
    }


    void OnDestroy()
    {
        // 清理音訊監聽
        if (receivedAudioTrack != null)
        {
            receivedAudioTrack.onReceived -= OnAudioDataReceived;
        }

        if (chatChannel != null)
        {
            chatChannel.Close();
            chatChannel.Dispose();
        }

        if (echoChannel != null)
        {
            echoChannel.Close();
            echoChannel.Dispose();
        }

        if (commandChannel != null)
        {
            commandChannel.Close();
            commandChannel.Dispose();
        }

        peerConnection?.Dispose();
    }
}
