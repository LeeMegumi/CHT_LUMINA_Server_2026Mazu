using UnityEngine;

public class UIController : MonoBehaviour
{
    public WebRTCManager webRTCManager;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        webRTCManager = GetComponent<WebRTCManager>();
    }

    // Update is called once per frame
    void Update()
    {
        /*if (Input.GetKeyUp(KeyCode.A))
        {
            webRTCManager.SendMessage("哈囉", "chat");
        }
        if (Input.GetKeyUp(KeyCode.S))
        {
            webRTCManager.SendMessage("你叫什麼名字", "chat");
        }
      
        if (Input.GetKeyUp(KeyCode.D))
        {
            //webRTCManager.SendMessage("那根據這支籤的內容，我適合當廚師嗎?", "chat");
            webRTCManager.SendMessage("How old are you?", "chat");
        }*/
    }
}
