using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class CommandReceiver : MonoBehaviour
{
    [System.Serializable]
    private class Command
    {
        public string type;
        public string action;
    }

    [SerializeField] private ActionPlayer player;
    [SerializeField] private int port = 5005;

    private UdpClient client;

    private void Start()
    {
        client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Debug.Log($"[CommandReceiver] ポート {port} で待機中");
    }

    private void Update()
    {
        while (client.Available > 0)
        {
            IPEndPoint sender = null;
            byte[] data = client.Receive(ref sender);
            string json = Encoding.UTF8.GetString(data);
            Handle(json);
        }
    }

    private void Handle(string json)
    {
        Command cmd;
        try
        {
            cmd = JsonUtility.FromJson<Command>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CommandReceiver] JSON として読めません: {json} ({e.Message})");
            return;
        }

        if (cmd.type == "play")
        {
            player.Play(cmd.action);
        }
        else
        {
            Debug.LogWarning($"[CommandReceiver] 知らない type: {cmd.type}");
        }
    }

    private void OnDestroy()
    {
        client?.Close();
    }
}