using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ChatManager : MonoBehaviour
{
    public static ChatManager instance {  get; private set; }
    [Header("UI References")]
    public ScrollRect scrollRect;
    public Transform contentParent;

    [Header("Message Prefabs")]
    public GameObject userMessagePrefab;
    public GameObject aiMessagePrefab;

    private void Start()
    {
        if(instance == null)
            instance = this;
    }
    // 新增使用者訊息（語音輸入後呼叫）
    public void AddUserMessage(string messageText)
    {
        CreateMessage(userMessagePrefab,"User", messageText);
        ScrollToBottom();
    }

    // 新增AI回覆訊息
    public void AddAIMessage(string messageText)
    {
        CreateMessage(aiMessagePrefab,"Lumina", messageText);
        ScrollToBottom();
    }

    private void CreateMessage(GameObject prefab, string nameText, string messageText)
    {
        GameObject newMessage = Instantiate(prefab, contentParent);

        // 設定Name
        Text[] texts = newMessage.GetComponentsInChildren<Text>();
        texts[0].text = nameText; // Name物件
        texts[1].text = messageText; // Message物件
        float height = texts[0].gameObject.GetComponent<RectTransform>().rect.height + texts[1].gameObject.GetComponent<RectTransform>().rect.height + 25;
        //Debug.Log($"Calculated message height: {height}");
        newMessage.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    // 自動滾動到最新訊息
    private void ScrollToBottom()
    {
        StartCoroutine(ScrollToBottomCoroutine());
    }

    private IEnumerator ScrollToBottomCoroutine()
    {
        // 等待佈局更新完成
        yield return new WaitForEndOfFrame();

        Canvas.ForceUpdateCanvases();

        VerticalLayoutGroup layoutGroup = contentParent.GetComponent<VerticalLayoutGroup>();
        ContentSizeFitter sizeFitter = contentParent.GetComponent<ContentSizeFitter>();

        if (layoutGroup != null)
            layoutGroup.CalculateLayoutInputVertical();
        if (sizeFitter != null)
            sizeFitter.SetLayoutVertical();

        // 設定為0會滾動到底部
        scrollRect.verticalNormalizedPosition = 0;

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)scrollRect.transform);
    }

    // 清空對話功能
    public void ClearAllMessages()
    {
        // 刪除Content下的所有子物件
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        // 重置ScrollRect位置
        scrollRect.verticalNormalizedPosition = 1;
    }
}
