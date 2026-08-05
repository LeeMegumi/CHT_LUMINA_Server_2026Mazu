using System.Collections;
using UnityEngine;

public class AvatarController : MonoBehaviour
{
    private AvatarAPIManager apiManager;
    private WebRTCManager webrtcManager;
    private UIController uiController;
    //public RenderTexture videoRT;
    void Start()
    {
        apiManager = gameObject.AddComponent<AvatarAPIManager>();
        webrtcManager = gameObject.AddComponent<WebRTCManager>();
        uiController = gameObject.AddComponent<UIController>();

        // 開始連線流程
        StartCoroutine(ConnectToAvatar());
    }

    IEnumerator ConnectToAvatar()
    {
        // 1. 取得TURN資訊和Token
        yield return apiManager.GetTurnInformation();

        // 2. 建立PeerConnection
        webrtcManager.CreatePeerConnection(apiManager.turnInfo);

        // 3. 建立並傳送Offer
        yield return webrtcManager.CreateAndSendOffer();

        Debug.Log("Avatar連線完成！");
    }
}
