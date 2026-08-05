using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class ClientSession
{
    public string clientId; //連線者ID
    public TcpClient tcp; //連線者的Tcp線路
    public NetworkStream stream;
    public float lastHeartbeatTime;

    StreamWriter writer;

    public void Init()
    {
        writer = new StreamWriter(stream, Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public void Send(NetMessage msg)
    {
        if (writer == null) Init();

        string json = JsonUtility.ToJson(msg);
        writer.WriteLine(json);
    }
}
