﻿using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using VRC.Core;
using VRC.DataModel;
using WebSocketSharp;
using static VRCWSLibary.Strings;

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
        internal bool isAlive = false;

        public void Connect(string server)
        {

            this.server = server;
            AsyncUtilsVRCWS.ToMain(() =>
            {
                Disconnect();

                MelonLogger.Msg($"Connecting to {server}");
                ws = new WebSocket(server);
                ws.OnMessage += Recieve;
                ws.OnError += Reconnect;
                ws.OnClose += (_, close) => { connected = false; isAlive = false; if (!close.WasClean) Reconnect(null, null); };
                ws.EmitOnPing = false;
                ws.OnOpen += (_, _2) => {
                    isAlive = true;
                    MelonLogger.Msg($"Connected to {server}");
                    MelonCoroutines.Start(WaitForID());
                };
                ws.ConnectAsync();
            });
            

        }

        public async void Register(string userID)
        {
            var result = await SendWithResponse(new Message() { Method = RegisterString, Target = userID });
            if (result.Method != RegisterChallengeString)
            {
                MelonLogger.Msg("Something went wrong while registering. Abording");
                return;
            }
            string oldBio = APIUser.CurrentUser.bio;
            APIUser.CurrentUser.UpdateBio(result.Content, new Action(async() => {
                string csr = SecurityContext.PemEncodeSigningRequest(SecurityContext.CreateCSR(userID));
                var result2 = await SendWithResponse(new Message() { Method = RegisterChallengeCompletedString, Content = csr });

            }), new Action<string>((y) => {}));
        }

        public IEnumerator WaitForID()
        {
            while (VRCPlayer.field_Internal_Static_VRCPlayer_0 == null || VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_3 == null)
                yield return null;

            
            string userID = VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_String_3;
            X509Certificate2 userCert = SecurityContext.GetCertificate(userID);

            if (userCert == null)
            {
                // request from user
                Register(userID);
            }

            Login(userID, userCert);
        }

        public async void Login(string userID, X509Certificate2 userCert)
        {
            MelonLogger.Msg("Logging in");

            LoginMessage message = new LoginMessage() { Certificate = Convert.ToBase64String(userCert.RawData), Signature = SecurityContext.Sign("userID") };

            var result = await SendWithResponse(new Message() { Method = LoginString, Content = JsonConvert.SerializeObject(message) });

            if(result.Method == ConnectedString)
            {
                SetWorldID();
            }
            else{
                //Register again 
            }

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
            Send(JsonConvert.SerializeObject(msg));
        }

        private readonly Dictionary<Guid, TaskCompletionSource<Message>> pendingMessages = new Dictionary<Guid, TaskCompletionSource<Message>>();
        
        public async Task<Message> SendWithResponse(Message msg, long timeout = 2000)//2 seconds
        {
            Send(JsonConvert.SerializeObject(msg));
            TaskCompletionSource<Message> tcs = new TaskCompletionSource<Message>();
            pendingMessages[msg.ID] = tcs;

            if (await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromMilliseconds(timeout))) == tcs.Task)
            {
                return await tcs.Task;
            }
            else
            {
                pendingMessages.Remove(msg.ID);
                throw new TimeoutException("No response recieved in timeframe");
            }
        }

        public void Disconnect()
        {
            MelonLogger.Msg("Disconnecting");
            connected = false;
            //AsyncUtils.YieldToMainThread();
            if (ws != null)
                ws.Close();

        }

        public void Send(string msg)
        {
            AsyncUtilsVRCWS.ToMain(()=> {
                if (ws != null && isAlive)
                {
                    ws.Send(msg);
                }
                else
                {
                    MelonLogger.Msg("Couldnt send " + msg);
                }
            });
            

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
            MelonCoroutines.Start(RetryConnect(retryCount * 2));
        }
        public IEnumerator RetryConnect(int waititt)
        {
            
            yield return new WaitForSeconds(waititt);
            
            if (!connected)
                Connect(server);

        }

        public void Recieve(object sender, MessageEventArgs e)
        {
            //MelonLogger.Msg(e.Data);
            Message msg = null;
            try
            {
                msg = JsonConvert.DeserializeObject<Message>(e.Data);
            }
            catch (Exception)
            {
                //should never happen as message is always send by client. Maybe if client and server have diffrent versions
                return;
            }

            if(pendingMessages.ContainsKey(msg.ID))
            {
                pendingMessages[msg.ID].SetResult(msg);
                pendingMessages.Remove(msg.ID);
            }

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
                MelonLogger.Msg("Logged in");
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

            if (item != null)
                Client.Methods[item]?.Invoke(msg);
        }
    }
}
