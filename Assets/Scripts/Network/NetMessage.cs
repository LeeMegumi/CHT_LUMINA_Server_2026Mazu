[System.Serializable]
public class NetMessage
{
    public string type;
    public string clientId;

    // command
    public string action;
    public float time;

    // server -> client
    public string param;
    public int luckynum;
    public int coinState;
}