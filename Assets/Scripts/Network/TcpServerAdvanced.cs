using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEngine.UI;

public class TcpServerAdvanced : MonoBehaviour
{
    TcpListener listener;
    Thread listenThread;

    Dictionary<string, ClientSession> clients = new();

    public Text clientListText;

    public ServerMain main;

    void Start()
    {
        listenThread = new Thread(ListenLoop);
        listenThread.IsBackground = true;
        listenThread.Start();

        InvokeRepeating(nameof(BroadcastServerStatus), 1f, 2f);

    }

    void Update()
    {
        clientListText.text = string.Join("\n", clients.Keys);

        List<string> timeout = new();
        foreach (var c in clients)
        {
            if (Time.time - c.Value.lastHeartbeatTime > 5f)
                timeout.Add(c.Key);
        }

        foreach (var id in timeout)
        {
            clients[id].tcp.Close();
            clients.Remove(id);
        }

    }

    void ListenLoop()
    {
        listener = new TcpListener(IPAddress.Any, 8888);
        listener.Start();

        while (true)
        {
            TcpClient tcp = listener.AcceptTcpClient();
            Thread t = new Thread(() => HandleClient(tcp));
            t.IsBackground = true;
            t.Start();
        }
    }

    void HandleClient(TcpClient tcp)
    {
        NetworkStream stream = tcp.GetStream();
        StreamReader reader = new StreamReader(stream, Encoding.UTF8);

        while (tcp.Connected)
        {
            string line;
            try
            {
                line = reader.ReadLine();
            }
            catch
            {
                break;
            }

            if (string.IsNullOrEmpty(line)) break;

            NetMessage msg = JsonUtility.FromJson<NetMessage>(line);
            UnityMainThreadQueue.Enqueue(() => ProcessMessage(msg, tcp, stream));
        }

        tcp.Close();
    }

    void ProcessMessage(NetMessage msg, TcpClient tcp, NetworkStream stream)
    {
        if (msg.type == "heartbeat")
        {
            ClientSession session;

            if (!clients.TryGetValue(msg.clientId, out session))
            {
                session = new ClientSession
                {
                    clientId = msg.clientId,
                    tcp = tcp,
                    stream = stream
                };
                session.Init();
                clients[msg.clientId] = session;
            }

            session.lastHeartbeatTime = Time.time;

            // ⭐⭐ 每次 heartbeat 都回 Server Status（關鍵）
            SendServerStatus(session);
        }

        if (msg.type == "command")
        {
            Debug.Log(msg.action);
            if (msg.action == "PLAY") VideoController.Instance.Play();
            if (msg.action == "PAUSE") VideoController.Instance.Pause();
            if (msg.action == "WAKEUP") StartCoroutine(main.GotWakeUpAction());
            if (msg.action == "LOTTERY") main.StartLotteryAction();
            if (msg.action == "GETNUMBER") StartCoroutine(main.GETLotteryNumberAction(msg.luckynum));
            //if (msg.action == "TOSSINGFAILED") 
            if (msg.action == "FREEQA") main.TossingSuccessfulAction(msg.luckynum); //傳遞籤號，同時發送訊息給中華平台。
            if (msg.action == "RESET")
            {
                main.isResetting = true;
                main.ServerAllReset();
            }
            if(msg.action == "CoinActionA")
            {
                StartCoroutine(CoinFlipGame.instance.ShowCoinResult(CoinFlipGame.instance.coinAVideoPlayer, CoinFlipGame.instance.coinAAudioSource, CoinFlipGame.instance.coinACanvasGroup, msg.coinState));
                if (msg.coinState == 0)
                {
                    StartCoroutine(main.TossingFailedAction(msg.luckynum));
                    //硬幣動畫失敗播放後，淡出。
                }
            }
            if (msg.action == "CoinActionB")
            {
                StartCoroutine(CoinFlipGame.instance.ShowCoinResult(CoinFlipGame.instance.coinBVideoPlayer, CoinFlipGame.instance.coinBAudioSource, CoinFlipGame.instance.coinBCanvasGroup, msg.coinState));
                if (msg.coinState == 0)
                {
                    StartCoroutine(main.TossingFailedAction(msg.luckynum));
                    //硬幣動畫失敗播放後，淡出。
                }
            }
            if (msg.action == "CoinActionC")
            {
                StartCoroutine(CoinFlipGame.instance.ShowCoinResult(CoinFlipGame.instance.coinCVideoPlayer, CoinFlipGame.instance.coinBAudioSource,CoinFlipGame.instance.coinCCanvasGroup, msg.coinState));
                if (msg.coinState == 0)
                {
                    StartCoroutine(main.TossingFailedAction(msg.luckynum));
                    //硬幣動畫失敗播放後，淡出。
                }
            }
        }
    }
    void SendServerStatus(ClientSession session)
    {
        session.Send(new NetMessage
        {
            type = "server_status",
            param = SystemInfo.deviceName // 可重用 param
        });
    }

    void SendStatus()
    {
        var status = new
        {
            type = "status",
            serverName = SystemInfo.deviceName,
            connectedClients = new List<string>(clients.Keys)
        };

        string json = JsonUtility.ToJson(status) + "\n";
        byte[] data = Encoding.UTF8.GetBytes(json);

        foreach (var c in clients.Values)
        {
            c.stream.Write(data, 0, data.Length);
        }
    }
    public void SendCommandToAll(string action, string param = "")
    {
        foreach (var c in clients.Values)
        {
            c.Send(new NetMessage
            {
                type = "server_command",
                action = action,
                param = param
            });
        }
    }
    public void SendCommandToClient(string clientId, string action, string param = "")
    {
        if (!clients.ContainsKey(clientId)) return;

        clients[clientId].Send(new NetMessage
        {
            type = "server_command",
            action = action,
            param = param
        });
    }

    void BroadcastServerStatus()
    {
        foreach (var c in clients.Values)
        {
            SendServerStatus(c);
        }
    }

}
