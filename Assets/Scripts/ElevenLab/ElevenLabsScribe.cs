using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.IO;
using System;

public class ElevenLabsScribe : MonoBehaviour
{
    [Header("ElevenLabs 設定")]
    [SerializeField] private string apiKey = "YOUR_API_KEY_HERE"; // 在此填入你的 API Key

    [Header("UI 元件")]
    [SerializeField] private Button recordButton;
    [SerializeField] private Text statusText;
    [SerializeField] private Text transcriptionText;

    [Header("錄音設定")]
    [SerializeField] private int maxRecordingTime = 30; // 最大錄音時間（秒）
    [SerializeField] private int sampleRate = 16000; // 取樣率

    private AudioClip recordedClip;
    private string microphoneDevice;
    private bool isRecording = false;

    private const string SCRIBE_API_URL = "https://api.elevenlabs.io/v1/speech-to-text";

    void Start()
    {
        // 檢查是否有可用的麥克風
        if (Microphone.devices.Length > 0)
        {
            for (int i = 0; i < Microphone.devices.Length; i++)
            {
                //Debug.Log(i.ToString() + " mic:" + Microphone.devices[i].ToString());
            }
            microphoneDevice = Microphone.devices[0];
            UpdateStatus("準備就緒，點擊按鈕開始錄音");
        }
        else
        {
            UpdateStatus("錯誤：找不到麥克風裝置");
            recordButton.interactable = false;
            return;
        }

        // 設定按鈕事件
        recordButton.onClick.AddListener(ToggleRecording);
        UpdateButtonText();
    }

    void ToggleRecording()
    {
        if (isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    void StartRecording()
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
        {
            UpdateStatus("錯誤：請先設定 API Key");
            return;
        }

        isRecording = true;
        UpdateStatus("錄音中...");
        UpdateButtonText();
        transcriptionText.text = "";

        // 開始錄音
        recordedClip = Microphone.Start(microphoneDevice, false, maxRecordingTime, sampleRate);
    }

    void StopRecording()
    {
        if (!isRecording) return;

        isRecording = false;
        Microphone.End(microphoneDevice);
        UpdateStatus("處理中...");
        UpdateButtonText();

        // 傳送音訊到 ElevenLabs
        StartCoroutine(SendAudioToElevenLabs());
    }

    IEnumerator SendAudioToElevenLabs()
    {
        // 將 AudioClip 轉換為 WAV 格式的 byte array
        byte[] wavData = ConvertAudioClipToWav(recordedClip);

        if (wavData == null || wavData.Length == 0)
        {
            UpdateStatus("錯誤：音訊轉換失敗");
            yield break;
        }

        // 建立 multipart/form-data 請求
        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        formData.Add(new MultipartFormFileSection("file", wavData, "recording.wav", "audio/wav"));
        formData.Add(new MultipartFormDataSection("model_id", "scribe_v1")); // 注意是底線不是破折號
        formData.Add(new MultipartFormDataSection("language_code", "zh")); // 繁體中文語言碼

        UnityWebRequest request = UnityWebRequest.Post(SCRIBE_API_URL, formData);
        request.SetRequestHeader("xi-api-key", apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string jsonResponse = request.downloadHandler.text;
            Debug.Log($"API 回應：{jsonResponse}");
            ProcessTranscription(jsonResponse);
        }
        else
        {
            UpdateStatus($"錯誤：{request.error}");
            Debug.LogError($"API 錯誤：{request.downloadHandler.text}");
        }
    }

    void ProcessTranscription(string jsonResponse)
    {
        try
        {
            // 解析 JSON 回應
            ScribeResponse response = JsonUtility.FromJson<ScribeResponse>(jsonResponse);

            if (!string.IsNullOrEmpty(response.text))
            {
                transcriptionText.text = response.text;
                UpdateStatus("辨識完成！");
            }
            else
            {
                UpdateStatus("未辨識到文字內容");
            }
        }
        catch (Exception e)
        {
            UpdateStatus("錯誤：解析回應失敗");
            Debug.LogError($"解析錯誤：{e.Message}\n回應內容：{jsonResponse}");
        }
    }

    byte[] ConvertAudioClipToWav(AudioClip clip)
    {
        if (clip == null) return null;

        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        // 轉換為 16-bit PCM
        short[] intData = new short[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];

        float rescaleFactor = 32767;

        for (int i = 0; i < samples.Length; i++)
        {
            intData[i] = (short)(samples[i] * rescaleFactor);
            byte[] byteArr = BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2);
        }

        // 建立 WAV 檔案
        using (MemoryStream stream = new MemoryStream())
        {
            // WAV 標頭
            int channels = clip.channels;
            int sampleRate = clip.frequency;

            stream.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"), 0, 4);
            stream.Write(BitConverter.GetBytes(bytesData.Length + 36), 0, 4);
            stream.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"), 0, 4);
            stream.Write(System.Text.Encoding.UTF8.GetBytes("fmt "), 0, 4);
            stream.Write(BitConverter.GetBytes(16), 0, 4);
            stream.Write(BitConverter.GetBytes((short)1), 0, 2);
            stream.Write(BitConverter.GetBytes((short)channels), 0, 2);
            stream.Write(BitConverter.GetBytes(sampleRate), 0, 4);
            stream.Write(BitConverter.GetBytes(sampleRate * channels * 2), 0, 4);
            stream.Write(BitConverter.GetBytes((short)(channels * 2)), 0, 2);
            stream.Write(BitConverter.GetBytes((short)16), 0, 2);
            stream.Write(System.Text.Encoding.UTF8.GetBytes("data"), 0, 4);
            stream.Write(BitConverter.GetBytes(bytesData.Length), 0, 4);
            stream.Write(bytesData, 0, bytesData.Length);

            return stream.ToArray();
        }
    }

    void UpdateStatus(string message)
    {
        statusText.text = message;
        Debug.Log(message);
    }

    void UpdateButtonText()
    {
        Text buttonText = recordButton.GetComponentInChildren<Text>();
        if (buttonText != null)
        {
            buttonText.text = isRecording ? "停止錄音" : "開始錄音";
        }
    }

    void OnDestroy()
    {
        // 清理
        if (isRecording)
        {
            Microphone.End(microphoneDevice);
        }
    }

    // JSON 回應結構
    [System.Serializable]
    public class ScribeResponse
    {
        public string text;
        public string language;
    }
}