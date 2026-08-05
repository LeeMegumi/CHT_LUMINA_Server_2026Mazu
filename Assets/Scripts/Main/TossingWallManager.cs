using UnityEngine;
using UnityEngine.UI;

public class TossingWallManager : MonoBehaviour
{
    public static TossingWallManager Instance { get; private set; }
    [SerializeField] private TossingWall canvas1_30;
    [SerializeField] private TossingWall canvas31_60;

    public Image LuckyContentImage; // 用於顯示籤詩內容的Image
    public Sprite[] LuckyCotents; // 存放籤詩內容的Sprite陣列，需在Inspector中設置

    private void Start()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    // 清除所有
    public void ClearAll()
    {
        canvas1_30.SetClearState();
        canvas31_60.SetClearState();
    }

    // 設置任意編號為Checking狀態
    public void SetCheckingNumber(int number)
    {
       
        // 先清除兩個Canvas
        canvas1_30.SetClearState();
        canvas31_60.SetClearState();

        // 根據編號範圍選擇對應的Canvas
        if (number >= 1 && number <= 30)
        {
            canvas1_30.SetCheckingState(number);
        }
        else if (number >= 31 && number <= 60)
        {
            canvas31_60.SetCheckingState(number);
        }
    }

    // 確認當前選擇
    public void ConfirmCurrent(int number)
    {
        Debug.Log("Confirming number: " + number);
        LuckyContentImage.sprite = LuckyCotents[number - 1]; // 根據編號顯示對應的籤詩內容
                                                             
        if (number >= 1 && number <= 30)
        {
            canvas1_30.SetConfirmState();
        }
        else if (number >= 31 && number <= 60)
        {
            canvas31_60.SetConfirmState();
        }
        
        
    }


}
