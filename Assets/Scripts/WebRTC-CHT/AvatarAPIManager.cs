using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json; // 需要安裝JSON.NET套件

public class AvatarAPIManager : MonoBehaviour
{
    //public string TURN_INFO_URL = "https://backend.avatar.cht.com.tw/streaming/getTurnInformation";
    //public string OFFER_URL = "https://prod-avatar.guide-next.com/v1/avatar/offer";
    //-------------------------------------------------------------------------------------
    public string TURN_INFO_URL = "https://prod-avatar.guide-next.com/v1/avatar/turn";
    public string OFFER_URL = "https://prod-avatar.guide-next.com/v1/avatar/offer";

    public string accessToken;
    public TurnInformation turnInfo;

    [System.Serializable]
    public class TurnInformation
    {
        public string username;
        public string credential;
        public string[] urls;
    }
   
    // Step 1: 取得TURN伺服器資訊
    public IEnumerator GetTurnInformation()
    {
        TURN_INFO_URL = "https://prod-avatar.guide-next.com/v1/avatar/turn";
        UnityWebRequest request = new UnityWebRequest(TURN_INFO_URL, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes("{}");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<TurnResponse>(request.downloadHandler.text);

            if (response.processResult)
            {
                 // 解析 processObject 中的 JSON 字串
                 var processObj = JsonUtility.FromJson<ProcessObject>(response.processObject);
                 turnInfo = processObj.turn_information;
                 accessToken = processObj.access_token;
                

                /*// processObject 是字串，需要再解析一次
                JObject processObj = JObject.Parse(response.processObject);


                // 取得 turn_information
                turnInfo = processObj["turn_information"].ToObject<TurnInformation>();


                // 取得 access_token
                accessToken = processObj["access_token"].ToString();*/

                Debug.Log($"解析後的 Token: {accessToken}");


                Debug.Log("TURN資訊取得成功");
                Debug.Log($"Access Token: {accessToken}");
            }
        }
        else
        {
            Debug.LogError($"取得TURN資訊失敗: {request.error}");
        }
    }


    [System.Serializable]
    class TurnResponse
    {
        public bool processResult;
        public string processResultCode;
        public string processResultMsg;
        public string processObject;
    }

    [System.Serializable]
    class ProcessObject
    {
        public TurnInformation turn_information;
        public string access_token;
    }
}
