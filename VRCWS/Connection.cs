using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VRChatUtilityKit.Utilities;
using WebSocketSharp;

namespace VRCWSLibary
{
    public class Connection
    {
        private WebSocket ws;
        private readonly Client Client;

        public Connection(Client client)
        {
            this.Client = client;
        }


        private string server;
        public int retryCount = 0;
        internal bool connected;

        public async void Connect(string server)
        {

            this.server = server;
            await AsyncUtils.YieldToMainThread();
            Disconnect();

            MelonLogger.Msg($"Connecting to {server}");
            ws = new WebSocket(server);
            ws.OnMessage += Recieve;
            ws.OnError += Reconnect;
            ws.OnClose += (_, close) => { connected = false; if (!close.WasClean) Reconnect(null, null); };
            ws.EmitOnPing = false;
            ws.OnOpen += (_, _2) => {
                MelonLogger.Msg($"Connected to {server}");
                MelonCoroutines.Start(SetUserID());
            };
            ws.ConnectAsync();

        }

        public IEnumerator SetUserID()
        {
            while (VRCPlayer.field_Internal_Static_VRCPlayer_0 == null || VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_2 == null)
                yield return null;

            string userID = VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_2;



            MelonLogger.Msg("Connecting as " + userID);
            Send(new Message() { Method = "StartConnection", Content = userID });
            SetWorldID();
        }
        public void SetWorldID()
        {

            //https://github.com/loukylor/VRC-Mods/blob/main/VRChatUtilityKit/Utilities/DataUtils.cs
            SHA256 sha256 = SHA256.Create();
            string hashedWorldID = Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(RoomManager.prop_String_0)));
            Send(new Message() { Method = "SetWorld", Content = hashedWorldID });
        }

        public void Send(Message msg)
        {
            SecurityContext.Sign(msg);
            Send(JsonConvert.SerializeObject(msg));
        }

        public void Disconnect()
        {
            MelonLogger.Msg("Disconnecting");
            connected = false;
            //AsyncUtils.YieldToMainThread();
            if (ws != null)
                ws.Close();

        }

        public async void Send(string msg)
        {
            await AsyncUtils.YieldToMainThread();
            
            if (ws != null && ws.IsAlive)
            {
                ws.Send(msg);
            }
            else
            {
                MelonLogger.Msg("Couldnt send " + msg);
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
            if (!connected)
                Connect(server);

        }

        public void Recieve(object sender, MessageEventArgs e)
        {
            MelonLogger.Msg(e.Data);
            Message msg = JsonConvert.DeserializeObject<Message>(e.Data);
            Client.OnMessage(msg);

            if (msg.Method == "OnlineStatus")
            {
                Client.OnOnline(msg.Target, msg.Content == "Online");
            }
            else if (msg.Method == "Error")
            {
                Client.OnError(msg);
            }
            else if (msg.Method == "MethodsUpdated")
            {
                //Nothing
            }
            else if (msg.Method == "WorldUpdated")
            {
                //Nothing
            }
            else if (msg.Method == "Connected")
            {
                connected = true;
                Client.OnConnected();
            }
            else if (msg.Method == "MethodAccept")
            {
                Client.OnMethodCheckResponseRecieved(msg.Content, msg.Target, true);
            }
            else if (msg.Method == "MethodDecline")
            {
                Client.OnMethodCheckResponseRecieved(msg.Content, msg.Target, false);
            }
            else
            {
                HandleCustomEvent(msg);
            }
        }

        private void HandleCustomEvent(Message msg)
        {
            var item = Client.Methods.Keys.FirstOrDefault(x => x.Method == msg.Method);

            if (item.SignatureRequired && !SecurityContext.Verify(msg))
            {
                MelonLogger.Msg("Ignoring message with invalid or untrusted signature");
                return;
            }

            if (item != null)
                Client.Methods[item]?.Invoke(msg);
        }
    }
}
