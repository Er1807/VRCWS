using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VRCWS;

[assembly: MelonInfo(typeof(VRCWSLibaryMod), "VRCWSLibary", "1.0.0", "Eric van Fandenfart")]
[assembly: MelonGame]


namespace VRCWS
{
    

    public class Message
    {
        public string Method { get; set; }
        public string Target { get; set; }
        public string Content { get; set; }

        public override string ToString()
        {
            return $"{Method} - {Target} - {Content}";
        }
    }

    public class VRCWSLibaryMod : MelonMod
    {
        public override void OnApplicationStart()
        {
            var category = MelonPreferences.CreateCategory("WSConnectionLibary");
            MelonPreferences_Entry<string> entry =  category.CreateEntry("Server", "wss://vrcws.er1807.de/VRC");
            entry.OnValueChanged += (oldValue, newValue) => { Client.GetClient().Connect(newValue); };
            Client.GetClient().Connect(entry.Value);
        }
    }

    public class Client
    {
        private static Client client;

        public static Client GetClient()
        {
            if (client == null) 
                client = new Client();
            return client;
        }

        public static bool ClientAvailable() => client != null && client.connected;

        private ClientWebSocket ws;
        public event MessageEvent MessageRecieved;
        public event ConnectEvent Connected;
        public bool connected = false;
        public event MessageEvent ErrorRecieved;
        public event OnlineEvent OnlineRecieved;
        public delegate void MessageEvent(Message message);
        public delegate void ConnectEvent();
        public delegate void OnlineEvent(string userID, bool online);

        public Dictionary<string, MessageEvent> Methods = new Dictionary<string, MessageEvent>();

        
        public Client()
        {
            Connected += () => {
                foreach (var item in Methods.Keys)
                {
                    AcceptMethod(item);
                }
            };
        }

        public void Connect(string server)
        {
            MelonLogger.Msg($"Connecting to {server}");
            connected = false;
            if(ws!= null && ws.State == WebSocketState.Open)
            {
                ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Restart", CancellationToken.None).Wait();
            }

            ws = new ClientWebSocket();
            ws.ConnectAsync(new Uri(server), CancellationToken.None).Wait();
            Task.Run(() => {
                Recieve();
            });
            MelonCoroutines.Start(SetUserID());
            MelonLogger.Msg($"Connected to {server}");
        }

        //https://forum.unity.com/threads/solved-dictionary-of-delegate-such-that-each-value-hold-multiple-methods.506880/
        public void RegisterEvent(string method, MessageEvent e)
        {
            MelonLogger.Msg($"Registering Event {method}");
            MessageEvent EventStored;
            if (Methods.TryGetValue(method, out EventStored))
            {
                EventStored += e;
                Methods[method] = EventStored; // Copy the newly aggregated delegate back into the dictionary.
            }
            else
            {
                EventStored += e;
                Methods.Add(method, EventStored);
                if(connected)
                    AcceptMethod(method);
            }
        }
        private void AcceptMethod(string method)
        {
            Send(new Message() { Method = "AcceptMethod", Content = method });
        }
        private void RemoveMethod(string method)
        {
            Send(new Message() { Method = "RemoveMethod", Content = method });
        }

        public void IsUserOnline(string userID)
        {
            Send(new Message() { Method = "IsOnline", Target = userID});
        }

        public void Send(Message msg)
        {
            Send(JsonConvert.SerializeObject(msg));
        }
        private void Send(string msg)
        {
            if(ws.State == WebSocketState.Open)
            {
                ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        public IEnumerator SetUserID()
        {
            while (VRCPlayer.field_Internal_Static_VRCPlayer_0 == null || VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_2 == null)
                yield return null;

            string userID = VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_2;
            Send(new Message() { Method = "StartConnection", Content = userID });
        }
        private async void Recieve()
        {
            try
            {
                const int maxMessageSize = 1024;
                byte[] receiveBuffer = new byte[maxMessageSize];
                while (ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult receiveResult = await ws.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        var receivedString = Encoding.UTF8.GetString(receiveBuffer, 0, receiveResult.Count);
                        MelonLogger.Msg(receivedString);
                        Message msg = JsonConvert.DeserializeObject<Message>(receivedString);
                        MessageRecieved?.Invoke(msg);
                        if (msg.Method == "OnlineStatus")
                        {
                            OnlineRecieved?.Invoke(msg.Target, msg.Content == "Online");
                        }
                        else if (msg.Method == "Error")
                        {
                            ErrorRecieved?.Invoke(msg);
                        }
                        else if (msg.Method == "MethodsUpdated")
                        {
                            //Nothing
                        }
                        else if (msg.Method == "Connected")
                        {
                            connected = true;
                            Connected?.Invoke();
                        }
                        else
                        {
                            if (Methods.ContainsKey(msg.Method))
                                Methods[msg.Method]?.Invoke(msg);
                        }
                    }
                }
            }
            catch (Exception)
            {
                MelonLogger.Msg("Reciever errored");
            }
        }
    }
}
