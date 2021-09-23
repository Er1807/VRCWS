using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using WebSocketSharp;
using VRChatUtilityKit.Utilities;
using VRCWSLibary;
using System.Linq;

[assembly: MelonInfo(typeof(VRCWSLibaryMod), "VRCWSLibary", "1.0.2", "Eric van Fandenfart")]
[assembly: MelonGame]


namespace VRCWSLibary
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
    public class AcceptedMethod
    {
        public string Method { get; set; }
        public bool WorldOnly { get; set; }

        public override string ToString()
        {
            return $"{Method} - {WorldOnly}";
        }
    }

    public class VRCWSLibaryMod : MelonMod
    {
        public override void OnApplicationStart()
        {
            var category = MelonPreferences.CreateCategory("WSConnectionLibary");
            MelonPreferences_Entry<string> entryURL = category.CreateEntry("Server", "wss://vrcws.er1807.de/VRC");
            MelonPreferences_Entry<bool> entryConnect = category.CreateEntry("Connect", false);
            entryURL.OnValueChanged += (oldValue, newValue) => { Client.GetClient().Connect(newValue); };
            entryConnect.OnValueChanged += (oldValue, newValue) => {
                Client.GetClient().retryCount = 0;
                if (newValue) Client.GetClient().Connect(entryURL.Value);
                else Client.GetClient().Disconnect();
            };
            if (entryConnect.Value)
                Client.GetClient().Connect(entryURL.Value);
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

        private WebSocket ws;
        public event MessageEvent MessageRecieved;
        public event ConnectEvent Connected;
        public bool connected = false;
        public event MessageEvent ErrorRecieved;
        public event OnlineEvent OnlineRecieved;
        public delegate void MessageEvent(Message message);
        public delegate void ConnectEvent();
        public delegate void OnlineEvent(string userID, bool online);

        public Dictionary<AcceptedMethod, MessageEvent> Methods = new Dictionary<AcceptedMethod, MessageEvent>();


        public Client()
        {
            Connected += () =>
            {
                foreach (var item in Methods.Keys)
                {
                    AcceptMethod(item);
                }
            };
        }

        

                
        private string server;
        public int retryCount = 0;

        public async void Connect(string server)
        {

            this.server = server;
            await AsyncUtils.YieldToMainThread();
            Disconnect();
           
            MelonLogger.Msg($"Connecting to {server}");
            ws = new WebSocket(server);
            ws.OnMessage += Recieve;
            ws.OnError += Reconnect;
            ws.OnClose += (_, close) => { connected = false;  if (!close.WasClean) Reconnect(null, null); };
            ws.EmitOnPing = false;
            ws.OnOpen += (_,_2) => {
                MelonCoroutines.Start(SetUserID());
                MelonLogger.Msg($"Connected to {server}");
            };
            ws.ConnectAsync();
            
        }


        public void Disconnect()
        { 
            MelonLogger.Msg("Disconnecting");
            connected = false;
            //AsyncUtils.YieldToMainThread();
            if(ws != null)
                ws.Close();
            
        }

        //https://forum.unity.com/threads/solved-dictionary-of-delegate-such-that-each-value-hold-multiple-methods.506880/
        public void RegisterEvent(string method, MessageEvent e, bool worldOnly = true)
        {
            MelonLogger.Msg($"Registering Event {method}");
            AcceptedMethod acceptedMethod = new AcceptedMethod() {Method = method, WorldOnly = worldOnly };
            MessageEvent EventStored;
            if (Methods.TryGetValue(acceptedMethod, out EventStored))
            {
                EventStored += e;
                Methods[acceptedMethod] = EventStored; // Copy the newly aggregated delegate back into the dictionary.
            }
            else
            {
                EventStored += e;
                Methods.Add(acceptedMethod, EventStored);
                if (connected)
                    AcceptMethod(acceptedMethod);
            }
        }
        private void AcceptMethod(AcceptedMethod method)
        {
            Send(new Message() { Method = "AcceptMethod", Content =$"{method};{method.WorldOnly}"  });
        }
        private void RemoveMethod(AcceptedMethod method)
        {
            Send(new Message() { Method = "RemoveMethod", Content = method.Method });
        }

        public void IsUserOnline(string userID)
        {
            Send(new Message() { Method = "IsOnline", Target = userID });
        }

        public void Send(Message msg)
        {
            Send(JsonConvert.SerializeObject(msg));
        }
        private async void Send(string msg)
        {
            await AsyncUtils.YieldToMainThread();
            if (ws.IsAlive)
            {
                ws.Send(msg);
            }
            else
            {
                MelonLogger.Msg("Couldnt send "+msg);
            }
            
        }
        public IEnumerator SetUserID()
        {
            while (VRCPlayer.field_Internal_Static_VRCPlayer_0 == null || VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_2 == null)
                yield return null;

            string userID = VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_2;
            
            

            MelonLogger.Msg("Connecting as " + userID);
            Send(new Message() { Method = "StartConnection", Content = userID });
            Send(new Message() { Method = "SetWorld", Content = RoomManager.prop_String_0 });

        }

        private void Recieve(object sender, MessageEventArgs e)
        {
            MelonLogger.Msg(e.Data);
            Message msg = JsonConvert.DeserializeObject<Message>(e.Data);
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
                var item = Methods.Keys.First(x => x.Method == msg.Method);
                if (item != null)
                    Methods[item]?.Invoke(msg);
            }
        }

        private void Reconnect(object sender, EventArgs e)
        {
            retryCount += 1;
            connected = false;
            if (retryCount >= 10)
            {
                MelonLogger.Msg("RetryCount to high. Reconnect from the Setting and/or choose a new Server");
                return;
            }
            MelonLogger.Msg("Retrying to establish connection");
            MelonCoroutines.Start(RetryConnect(retryCount * 10));
        }
        public IEnumerator RetryConnect(int waititt)
        {
            while (waititt == 0)
            {
                waititt -= 1;
                yield return null;
            }
            if(!connected)
                Connect(server);

        }

    }
}
