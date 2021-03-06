using MelonLoader;
using Newtonsoft.Json;
using System.Collections.Generic;
using VRCWSLibary;
using HarmonyLib;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using System;
using VRC.UI.Elements.Menus;
using System.Collections;
using VRC.DataModel.Core;
using TMPro;
using VRC.UI.Elements;

[assembly: MelonInfo(typeof(VRCWSLibaryMod), "VRCWSLibary", "1.1.8", "Eric van Fandenfart")]
[assembly: MelonGame]
[assembly: MelonAdditionalDependencies("VRChatUtilityKit")]


namespace VRCWSLibary
{


    public class Message
    {
        public string Method { get; set; }
        public string Target { get; set; }
        public string Content { get; set; }
        public string Signature { get; set; }
        public Guid ID { get; set; } = Guid.NewGuid();
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;

        public override string ToString()
        {
            return $"{Method} - {Target} - {Content}";
        }

        public T GetContentAs<T>()
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(Content);
            }
            catch (Exception)
            {
                //an invalid packed was tryed to be parsed. return null and let using function deal with it
                return default;
            }
        }
    }
    public class AcceptedMethod
    {
        public string Method { get; set; }
        public bool WorldOnly { get; set; }
        public bool SignatureRequired { get; set; }

        public override string ToString()
        {
            return $"{Method} - {WorldOnly} - {SignatureRequired}";
        }
    }

    public class VRCWSLibaryMod : MelonMod
    {
        public override void OnApplicationStart()
        {
            SecurityContext.LoadKeys();

            var category = MelonPreferences.CreateCategory("VRCWS");
            MelonPreferences_Entry<string> entryURL = category.CreateEntry("Server", "wss://vrcws.er1807.de/VRC");
            MelonPreferences_Entry<bool> entryConnect = category.CreateEntry("Connect", true);
            entryURL.OnValueChanged += (oldValue, newValue) => { Client.GetClient().connection.Connect(newValue); };
            entryConnect.OnValueChanged += (oldValue, newValue) => {
                Client.GetClient().connection.retryCount = 0;
                AsyncUtilsVRCWS.ToMain(() =>
                {
                    if (newValue) Client.GetClient().connection.Connect(entryURL.Value);
                    else Client.GetClient().connection.Disconnect();
                });
            };


            if (entryConnect.Value)
                Client.GetClient().connection.Connect(entryURL.Value);


            MelonPreferences_Entry<bool> entrypubKey = category.CreateEntry("Accept Public Key", false);
            entrypubKey.OnValueChanged += (oldValue, newValue) => {
                if (newValue) StartAcceptingKey();
                else StopAcceptingKeys();
            };

            if(entrypubKey.Value) 
                StartAcceptingKey();

            HarmonyInstance.Patch(typeof(NetworkManager).GetMethod("OnJoinedRoom"), new HarmonyMethod(typeof(Client).GetMethod("OnJoinedRoom")));
            MelonCoroutines.Start(WaitForUIInit());
        }

        private IEnumerator WaitForUIInit()
        {
            while (VRCUiManager.prop_VRCUiManager_0 == null)
                yield return null;
            while (GameObject.Find("UserInterface").transform.Find("Canvas_QuickMenu(Clone)/Container/Window/QMParent") == null)
                yield return null;

            LoadUI();
        }

        private MenuStateController menuStateController;
        private void LoadUI()
        {
            menuStateController = GameObject.Find("UserInterface").transform.Find("Canvas_QuickMenu(Clone)").GetComponent<MenuStateController>();
            //based on VRCUKs code
            var camera = menuStateController.transform.Find("Container/Window/QMParent/Menu_Camera/Scrollrect/Viewport/VerticalLayoutGroup/Buttons/Button_Screenshot");
            var useractions = menuStateController.transform.Find("Container/Window/QMParent/Menu_SelectedUser_Local/ScrollRect/Viewport/VerticalLayoutGroup/Buttons_UserActions");
            var createFreezeButton = GameObject.Instantiate(camera, useractions);
            createFreezeButton.GetComponent<Button>().onClick.RemoveAllListeners();
            createFreezeButton.GetComponent<Button>().onClick.AddListener(new Action(() =>
            {
                MelonLogger.Msg($"Sending public key");
                string userID = menuStateController.GetComponentInChildren<SelectedUserMenuQM>().field_Private_IUser_0.prop_String_0;
                SendPubKey(userID);
            }));
            createFreezeButton.GetComponentInChildren<TextMeshProUGUI>().text = "Send Pub Key";




        }
        public void StartAcceptingKey()
        {
            Client.GetClient().RegisterEvent("SendPubKey", AcceptPublicKey, true, false);
        }

        public void StopAcceptingKeys()
        {
            Client.GetClient().RemoveEvent("SendPubKey");
        }

        public void SendPubKey(string userID)
        {
            Client.GetClient().Send(new Message() { Method = "SendPubKey", Target = userID, Content = SecurityContext.GetPublicKeyAsJsonString() });
        }

        public void AcceptPublicKey(Message msg)
        {
            AsyncUtilsVRCWS.ToMain(() =>
            {
                VRCUiPopupManager.field_Private_Static_VRCUiPopupManager_0.Method_Public_Void_String_String_String_Action_String_Action_Action_1_VRCUiPopup_0(
                "Accept PubKey", $"Accept Public key from user {msg.Target}", "Decline", new Action(() => { VRCUiManager.prop_VRCUiManager_0.HideScreen("POPUP"); }), "Accept",new Action( () => { SecurityContext.AcceptPubKey(msg.Content, msg.Target); VRCUiManager.prop_VRCUiManager_0.HideScreen("POPUP"); }));
            });
        }

        public override void OnUpdate()
        {
            if (AsyncUtilsVRCWS._toMainThreadQueue.TryDequeue(out Action result))
                result.Invoke();
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

        public static bool ClientAvailable() => client != null && client.Connected;

        private static readonly List<Client> allClients = new List<Client>();

        public Connection connection;

        public event MessageEvent MessageRecieved;
        public event ConnectEvent ConnectRecieved;
        public bool Connected => connection.connected;
        public event MessageEvent ErrorRecieved;
        public event OnlineEvent OnlineRecieved;
        //This does not mean that also the signature is trusted. the server doesnt know what is trusted
        public event MethodCheckEvent MethodCheckResponseRecieved;
        public delegate void MessageEvent(Message message);
        public delegate void ConnectEvent();
        public delegate void OnlineEvent(string userID, bool online);
        public delegate void MethodCheckEvent(string method, string userID, bool accept);

        public Dictionary<AcceptedMethod, MessageEvent> Methods = new Dictionary<AcceptedMethod, MessageEvent>();


        public Client()
        {
            connection = new Connection(this);
            allClients.Add(this);
            ConnectRecieved += () =>
            {
                lock (Methods)
                {
                    foreach (var item in Methods.Keys)
                    {
                        AcceptMethod(item);
                    }
                }
            };
        }     
        

        public static void OnJoinedRoom()
        {
            foreach (var item in allClients)
            {
                if (item.Connected)
                {
                    item.connection.SetWorldID();
                }
            }
        }

        //https://forum.unity.com/threads/solved-dictionary-of-delegate-such-that-each-value-hold-multiple-methods.506880/
        public void RegisterEvent(string method, MessageEvent e, bool worldOnly = true, bool signatureRequired = true)
        {
            lock (Methods)
            {
                MelonLogger.Msg($"Registering Event {method}");
                AcceptedMethod acceptedMethod = new AcceptedMethod() { Method = method, WorldOnly = worldOnly, SignatureRequired = signatureRequired };
                if (Methods.TryGetValue(acceptedMethod, out MessageEvent EventStored))
                {
                    EventStored += e;
                    Methods[acceptedMethod] = EventStored; // Copy the newly aggregated delegate back into the dictionary.
                }
                else
                {
                    EventStored += e;
                    Methods.Add(acceptedMethod, EventStored);
                    if (Connected)
                        AcceptMethod(acceptedMethod);
                }
            }
        }

        public void RemoveEvent(string method)
        {
            lock (Methods)
            {
                AcceptedMethod acceptedMethod = new AcceptedMethod() { Method = method };
                Methods.Remove(acceptedMethod);
                RemoveMethod(acceptedMethod);
            }
        }

        private void AcceptMethod(AcceptedMethod method)
        {
            Send(new Message() { Method = "AcceptMethod", Content = JsonConvert.SerializeObject(method) });
        }
        private void RemoveMethod(AcceptedMethod method)
        {
            Send(new Message() { Method = "RemoveMethod", Content = JsonConvert.SerializeObject(method) });
        }

        public void IsUserOnline(string userID)
        {
            Send(new Message() { Method = "IsOnline", Target = userID });
        }

        public async Task<bool> IsUserOnlineAsyncResponse(string userID)
        {
            var response = await SendWithResponse(new Message() { Method = "IsOnline", Target = userID });

            return response.Content == "Online";

        }

        public void DoesUserAcceptMethod(string userID, string method)
        {
            Send(new Message() { Method = "DoesUserAcceptMethod", Target = userID, Content = method });
        }
        public async Task<bool> DoesUserAcceptMethodAsyncResponse(string userID, string method)
        {
            var response = await SendWithResponse(new Message() { Method = "DoesUserAcceptMethod", Target = userID, Content = method });

            return response.Method == "MethodAccept";

        }

        public void Send(Message msg)
        {
            connection.Send(msg);
        }
        public async Task<Message> SendWithResponse(Message msg)
        {
            return await connection.SendWithResponse(msg);
        }

        public void OnMessage(Message msg)
        {
            MessageRecieved?.Invoke(msg);
        }

        internal void OnOnline(string user, bool online)
        {
            OnlineRecieved?.Invoke(user, online);
        }

        internal void OnError(Message msg)
        {
            ErrorRecieved?.Invoke(msg);
        }

        internal void OnConnected()
        {
            ConnectRecieved?.Invoke();
        }
        internal void OnMethodCheckResponseRecieved(string method, string user, bool accept)
        {
            MethodCheckResponseRecieved?.Invoke(method, user, accept);
        }
    }
}
