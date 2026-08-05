using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static ServerMain;
using static WebRTCManager;
using Random = UnityEngine.Random;

public class ServerMain : MonoBehaviour
{
    public static ServerMain instance { get; private set; }
    [Header("Network System")]
    public TcpServerAdvanced TcpServer;
    [Header("TTS System")]
    public ElevenLabs_VAD TTS_System;

    [Header("Lumina Animator")]
    public LuminaCharatorAnimatorController Lumina_Animtor;

    public Lumina_Custom_Audio LuminaAudio;

    [Header("指引文字文字")]
    public Text UI_TipText;

    
    [Header("UI動畫")]
    public Animator UI_Animtor;

    [Header("問答次數顯示文字")]
    public Text QACountText;
    [Header("問答次數")]
    public int QACount;

    [Header("問答倒數系統")]
    public CountdownBarController CountDownTimer;

    [Header("ARD")]
    public ArduinoBasic ARD;

    [Header("重製冷卻時間")]
    public bool isResetting = false;
    public float constResetCooldown = 5f;
    public float ResetCooldown;

    public enum Stage
    {
        waitforDeal,
        Sleep,  //休眠等待
        Opening,
        Lottery,
        TossingGame,  //
        TossingFailed,
        TossingSuccessful,
        FreeQA,
        End,

        ChatMode
    }
    public Stage currentStage;

    public enum InteractMode
    {
        Normal,
        Safe
    }
    public InteractMode interactMode;

    public GameObject SafeTag;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(instance == null) instance = this;
        _init();
    }

    // Update is called once per frame
    void Update()
    {
        //TalkDetect();
        RestCounter(); //防止快速重製
        if(Input.GetKeyUp(KeyCode.Escape))
        {
            SceneManager.LoadScene(0); //重新載入 
        } 
        if (Input.GetKeyUp(KeyCode.Y))
        {
            AvatarSkipConversation();
        } //Skip
        if (Input.GetKeyUp(KeyCode.R) && !isResetting)
        {
            AvatarSkipConversation();
            LuminaAudio.AudioStop();
            isResetting = true;
            TcpServer.SendCommandToAll("RESET");
            TcpServer.SendCommandToAll("NORMALMODE");
            ServerAllReset();
            UI_TipText.text = "LUMINA待機中！";   //Canvas 擲筊說明UI
        } //Reset
        if (Input.GetKeyUp(KeyCode.C))
        {
            TcpServer.SendCommandToAll("RESET");
            ServerAllReset();
            NextStage(Stage.ChatMode);
            QACount = int.MaxValue;
            UI_Animtor.Play("Chatting");
            TcpServer.SendCommandToAll("CHAT");
            UI_TipText.text = "與LUMINA自由問答中！";   //Canvas 擲筊說明UI
            Lumina_Animtor.NormalIdleLoop = true;
            Lumina_Animtor.PlaySingleAnimation("W-2 Final", true, null, LuminaCharatorAnimatorController.LoopMode.NormalIdle);
        } 
        //Chat
        if (Input.GetKeyUp(KeyCode.S))
        {
            interactMode = interactMode == InteractMode.Normal ? InteractMode.Safe : InteractMode.Normal;
            SafeTag.SetActive(interactMode == InteractMode.Normal ? false : true);
            if(interactMode == InteractMode.Normal)
            {
                TcpServer.SendCommandToAll("NORMALMODE");
            }
            else
            {
                TcpServer.SendCommandToAll("SAFEMODE");
            }
        } 
        //Safe
        
        switch (currentStage)
        {
           case Stage.FreeQA:
                //問答中
                TalkDetect();
                break;

            case Stage.ChatMode:
                //問答中
                TalkDetect();
                break;
        }
    }
    public void NextStage(Stage nextstage)=> currentStage = nextstage;

    void _init()
    {
        interactMode = InteractMode.Normal;
        SafeTag.SetActive(interactMode == InteractMode.Normal ? false : true);
        TcpServer.SendCommandToAll("NORMALMODE");
        CountDownTimer.ResetAndPause(); //問答倒數重製
        currentStage = Stage.Sleep;
        
        QACount = 5;
        QACountText.text = "剩餘問答次數：" + QACount;
        ResetCooldown = constResetCooldown;
        isResetting = false;
        TcpServer.SendCommandToAll("RESET");
        Lumina_Animtor.SleepIdleLoop = true;
        Lumina_Animtor.PlaySingleAnimation("W-2 Final", true, null, LuminaCharatorAnimatorController.LoopMode.SleepIdle);
    }
    /// <summary>
    /// 重製對話
    /// </summary>
    public void AvatarClearConversation()
    {
        var resetCommand = new CommandData
        {
            cmd = "res_1",
            arg = new ResetArg { reason = "conversation" },
            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            v = 1
        };

        WebRTCManager.instance.SendJsonMessage(resetCommand, "command");
    }
    /// <summary>
    /// 跳過對話，進入下一段對話
    /// </summary>
    public void AvatarSkipConversation()
    {
        // 建立 skip 指令
        var skipCommand = new CommandData
        {
            cmd = "skip",
            arg = new SkipArg { reason = "user_interrupt" },
            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            v = 1
        };

        // 發送
        WebRTCManager.instance.SendJsonMessage(skipCommand, "command");

    }

    //----------------------------------------Server發送指令至Client-------------------------------



    //----------------------------------------接收到Client端指令，執行功能-------------------------------
    //----------------------------------------------------//
    //所有由Client主程式傳送到Server的指令如下：
    //TcpClient.SendActionToServer("WAKEUP"); //傳送給Server，請LUMINA打招呼。
    //TcpClient.SendActionToServer("LOTTERY"); //傳送給Server，通知"現在要抽籤了"。
    //TcpClient.SendActionToServer("GETNUMBER", coinGame.LucykNumber); //傳遞"取得籤號"資訊給主機... (待定) 播放3D動畫、UI動畫、改變籤詩牆面燈號(打開閃爍)。
    //TcpClient.SendActionToServer("TOSSINGFAILED", coinGame.LucykNumber); //傳遞"擲筊失敗"資訊給主機... (待定) 播放3D動畫、UI動畫、改變籤詩牆面燈號(關閉閃爍)。
    //TcpClient.SendActionToServer("FREEQA",coinGame.LucykNumber); //傳遞籤號資訊給主機，要求進入解籤環節。  播放3D動畫、UI動畫、改變籤詩牆面燈號(恆亮)。
    //TcpClient.SendActionToServer("RESET"); //傳送給Server，狀態重置到初始狀態Sleep。

    /// <summary>
    /// Sleep中，搖晃設備喚醒
    /// </summary>
    public IEnumerator GotWakeUpAction()
    {
        if(currentStage != Stage.Sleep) yield break; //如果不在Sleep狀態，則不執行喚醒動作
        NextStage(Stage.Opening);  //進入喚醒狀態
        AvatarSkipConversation();
        float audioLength = LuminaAudio.LuminaAudioClip_Open.length;
        LuminaAudio.PlayCustomAudio(LuminaAudio.LuminaAudioClip_Open); //嘴型跟音檔。
        ChatManager.instance.AddAIMessage("人工智慧通玄理，命運未來啟鴻圖，歡迎來到抽籤未來！");
        Lumina_Animtor.PlaySingleAnimation("OP-01", returnToLoop: true, returnLoopMode: LuminaCharatorAnimatorController.LoopMode.NormalIdle);
        UI_Animtor.Play("SleepToOpen");  //UI動畫
        yield return new WaitForSeconds(audioLength); //等待音檔播放完畢
        TcpServer.SendCommandToAll("SERVERCALLBACK");  //通知Client端，動畫結束了，可以進入下一步了。
        NextStage(Stage.Lottery);  //進入抽籤環節
        UI_TipText.text = "請搖晃手上的LUMINA籤筒！";   //Canvas 擲筊說明UI
    }
    /// <summary>
    /// 接收到搖晃籤筒訊號的動作
    /// </summary>
    public void StartLotteryAction()
    {
        UI_TipText.text = "搖啊搖，搖到什麼籤～";
        Lumina_Animtor.PlaySingleAnimation("HL-C00", returnToLoop: true, returnLoopMode: LuminaCharatorAnimatorController.LoopMode.NormalIdle); //搖晃籤筒的動畫
    }
    /// <summary>
    /// 抽到籤號後，提醒搖晃設備擲筊
    /// </summary>
    public IEnumerator GETLotteryNumberAction(int LuckyNumber)
    {
        UI_TipText.text = "請問LUMINA，我的命運是屬於是這支，第" + LuckyNumber + "籤嗎？";
        TossingWallManager.Instance.SetCheckingNumber(LuckyNumber);  //新增閃爍狀態
        int randomIndex = Random.Range(0, LuminaAudio.LuminaAudioClips_Tossing.Length);
        float audioLength = LuminaAudio.LuminaAudioClips_Tossing[randomIndex].length;
        LuminaAudio.PlayCustomAudio(LuminaAudio.LuminaAudioClips_Tossing[randomIndex]); //嘴型跟音檔。
        switch(randomIndex)
        {
            case 0:
                ChatManager.instance.AddAIMessage("現在，你已經抽出了一支籤，但這支籤是不是屬於你，我們還需要擲筊確認。深呼吸集中精神，請再次搖動手中的道具！");
                break;
            case 1:
                ChatManager.instance.AddAIMessage("好像有一隻籤回應了你的呼喚喔，但我們還要再確認是否屬於你的命運，請使用籤之呼吸全集中，再次搖動手中的道具！");
                break;
            case 2:
                ChatManager.instance.AddAIMessage("你已經抽出一支籤！但是先別急！這支籤是不是「命中注定屬於你」，還需要擲筊來認證，請再搖一下手中的道具～");
                break;
        }

        Lumina_Animtor.PlaySingleAnimation("HL-C02", returnToLoop: true, returnLoopMode: LuminaCharatorAnimatorController.LoopMode.NormalIdle); //抽到什麼籤的動畫。

        NextStage(Stage.waitforDeal);
        yield return new WaitForSeconds(audioLength); //等待音檔播放完畢
        TcpServer.SendCommandToAll("SERVERCALLBACK");
        NextStage(Stage.TossingGame);  //進入擲筊遊戲
        UI_TipText.text = "擲筊中！";
        NextStage(Stage.waitforDeal);
        UI_Animtor.Play("ToTossingGame");  //UI動畫
        yield return null; //等待音檔播放完畢
    }
    
    /// <summary>
    /// 擲筊失敗後的動作
    /// </summary>
    public IEnumerator TossingFailedAction(int LuckyNumber)
    {
        UI_TipText.text = "很可惜這支籤跟你不是很合，讓我們重新再抽一支！";
        NextStage(Stage.TossingFailed);
        UI_Animtor.Play("TossingFaild");  //UI動畫
        Lumina_Animtor.PlaySingleAnimation("HL-F12", returnToLoop: true, returnLoopMode: LuminaCharatorAnimatorController.LoopMode.NormalIdle); //抽到什麼籤的動畫。

        TossingWallManager.Instance.ClearAll();  //清除閃爍狀態
        int randomIndex = Random.Range(0, LuminaAudio.LuminaAudioClips_TossingFailed.Length);
        float audioLength = LuminaAudio.LuminaAudioClips_TossingFailed[randomIndex].length;
        LuminaAudio.PlayCustomAudio(LuminaAudio.LuminaAudioClips_TossingFailed[randomIndex]); //嘴型跟音檔。
        switch(randomIndex)
        {
            case 0:
                ChatManager.instance.AddAIMessage("沒關係，這只是你心中所問之事還沒有上達天聽或不夠清晰完整，請在心裡重新默念一次之後就可以了，請貴客再次搖動手中道具吧。！");
                break;
            case 1:
                ChatManager.instance.AddAIMessage("沒關係，這只是你心中所問之事還沒有上達天聽或不夠清晰完整，請在心裡重新默念一次之後就可以了，請貴客再次搖動手中道具吧！");
                break;
            case 2:
                ChatManager.instance.AddAIMessage("欸…上面已讀不回？等等，那個微笑，很微妙欸～不是拒絕，但也還沒答應！ 這是「再試一次看看」的臉，讓我們再抽一次吧！ ");
                break;
        }

        Lumina_Animtor.PlaySingleAnimation("Re-Q", returnToLoop: true, returnLoopMode: LuminaCharatorAnimatorController.LoopMode.NormalIdle); //抽到什麼籤的動畫。
        yield return new WaitForSeconds(audioLength); //等待音檔播放完畢
        CoinFlipGame.instance.ResetCoins();
        TcpServer.SendCommandToAll("SERVERCALLBACK");  //通知Client端，動畫結束了，可以進入下一步了。
        NextStage(Stage.Lottery);  //進入擲筊遊戲                                                              
        //Canvas 擲筊說明UI

    }
    public void TossingSuccessfulAction(int LuckyNumber)
    {
        UI_TipText.text = "讓LUMINA幫你看看第" + LuckyNumber + "籤是什樣的命運吧～";
        NextStage(Stage.waitforDeal);
        UI_Animtor.Play("TossingSuccessful");  //UI動畫
        AudioPlayer.instance.PlayAudio(2, 3);
        TossingWallManager.Instance.ConfirmCurrent(LuckyNumber);  //清除閃爍狀態，確認籤詩
        SendLuckyNumToCHT(LuckyNumber);
        Lumina_Animtor.PlaySingleAnimation("HL-E01", true, () => //擲筊成功的動畫。

        {
            //動畫結束後要做的事情
            ARD.readMessage = "";
            NextStage(Stage.FreeQA);  //進入擲筊遊戲                                                              
            UI_Animtor.Play("ToQA");  //UI動畫
            //Canvas 擲筊說明UI
            UI_TipText.text = "LUMINA正在為您解籤！";
            CountDownTimer.ResetAndStart(); //開始計時
        }, returnLoopMode: LuminaCharatorAnimatorController.LoopMode.NormalIdle);
    }
    /// <summary>
    /// 傳送籤號給CHT AI後台
    /// </summary>
    /// <param name="luckynumData"></param>
    public void SendLuckyNumToCHT(int luckynumData)
    {
        string NumberText = FontConvert.NumberToChinese(luckynumData);
        WebRTCManager.instance.SendMessage("我抽到了第" + NumberText + "籤，可以幫我解籤嗎?", "chat");

    }

    public IEnumerator EndAction()
    {
        NextStage(Stage.End);  //進入喚醒狀態
        UI_Animtor.Play("ToEnd");  //UI動畫
        yield return new WaitForSeconds(.5F);  //等待截斷的聲音完全靜止
        int randomIndex = Random.Range(0, LuminaAudio.LuminaAudioClips_End.Length);
        float audioLength = LuminaAudio.LuminaAudioClips_End[randomIndex].length;
        LuminaAudio.PlayCustomAudio(LuminaAudio.LuminaAudioClips_End[randomIndex]); //嘴型跟音檔。
        switch(randomIndex)
        {
            case 0:
                ChatManager.instance.AddAIMessage("我的靈力用盡，一滴都不剩了，好啦好啦，最後給您一個改善運勢的小秘訣喔～申辦中華電信的相關業務，相信對你的運勢會更有幫助喔～ Lumi！");
                break;
            case 1:
                ChatManager.instance.AddAIMessage("天機不可洩漏，再多我就吐血了，好啦好啦，最後給您一個改善運勢的小秘訣喔～申辦中華電信的相關業務，相信對你的運勢會更有幫助喔～，謝謝貴客，可以附近逛逛唷～ Lumi！");
                break;
            case 2:
                ChatManager.instance.AddAIMessage("啊我沒電了，關機中，好啦好啦，最後給您一個改善運勢的小秘訣喔～申辦中華電信的相關業務，相信對你的運勢會更有幫助喔～謝謝貴客，可以附近逛逛唷～Lumi！ ");
                break;
        }
        Lumina_Animtor.PlaySingleAnimation("END", returnLoopMode: LuminaCharatorAnimatorController.LoopMode.SleepIdle);
        yield return new WaitForSeconds(audioLength); //等待音檔播放完畢
        NextStage(Stage.Sleep);  //進入抽籤環節
        UI_TipText.text = "請搖晃手上的LUMINA籤筒，喚醒LUMINA！";  //Canvas 擲筊說明UI
        TcpServer.SendCommandToAll("SERVERCALLBACK");  //通知Client端，動畫結束了，可以進入下一步了。
        TcpServer.SendCommandToAll("RESET");
        ServerAllReset();
    }
    void TalkDetect()
    {
        if (Input.GetKeyUp(KeyCode.T) && TTS_System.isRecording && TTS_System.Talkbool())
        {
            TTS_System.StopRecording();
            return;
        }
        if ((Input.GetKeyUp(KeyCode.T) || ARD.readMessage == "Coin") && TTS_System.Talkbool() && !TTS_System.isRecording)
        {
            ARD.readMessage = "";
            if (QACount > 0 && CountDownTimer.remainingTime > 0)
            {
                QACount--;
                QACountText.text = "剩餘問答次數：" + QACount;
                TTS_System.ToggleRecording();
                AudioPlayer.instance.PlayAudio(0); //Play Coin Talk Audio
                AvatarSkipConversation();
            }
            else
            {
                AvatarSkipConversation();
                AudioPlayer.instance.PlayAudio(3); //Play Energe Empty 
                StartCoroutine(EndAction());
            }
        }

    }
    /// <summary>
    /// 重製，並回到Sleep狀態。
    /// </summary>
    public void ServerAllReset()
    {
        currentStage = Stage.Sleep;
        ARD.readMessage = "";
        
        TossingWallManager.Instance.ClearAll();  //新增閃爍狀態
        QACount = 5;
        QACountText.text = "剩餘問答次數：" + QACount;
        ChatManager.instance.ClearAllMessages();
        CoinFlipGame.instance.ResetCoins();
        AvatarClearConversation();
        interactMode = InteractMode.Normal;
        SafeTag.SetActive(interactMode == InteractMode.Normal ? false : true);
        CountDownTimer.ResetAndPause(); //問答倒數重製
        Lumina_Animtor.SleepIdleLoop = true;
        Lumina_Animtor.PlaySingleAnimation("W-2 Final", true, null, LuminaCharatorAnimatorController.LoopMode.SleepIdle);
    }

    void RestCounter()
    {
        if (isResetting)
        {
            ResetCooldown -= Time.deltaTime;
            if (ResetCooldown <= 0)
            {
                isResetting = false;
                ResetCooldown = constResetCooldown;
            }
        }
    }
}
