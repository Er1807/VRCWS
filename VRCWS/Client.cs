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
using static VRCWSLibary.Strings;

[assembly: MelonInfo(typeof(VRCWSLibaryMod), "VRCWSLibary", "1.1.6", "Eric van Fandenfart")]
[assembly: MelonGame]
[assembly: MelonAdditionalDependencies("VRChatUtilityKit")]


namespace VRCWSLibary
{
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


            MelonPreferences_Entry<bool> allowFriendRequests = category.CreateEntry("Allow Friend Requests", true);
            allowFriendRequests.OnValueChanged += (oldValue, newValue) => {
                if (newValue) StartAcceptingFriendRequests();
                else StopAcceptingFriendRequests();
            };

            if(allowFriendRequests.Value) 
                StartAcceptingFriendRequests();

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
            BindingExtensions.Method_Public_Static_ButtonBindingHelper_Button_Action_0(createFreezeButton.GetComponent<Button>(), new Action(() =>
            {
                MelonLogger.Msg($"Sending public key");
                string userID = menuStateController.GetComponentInChildren<SelectedUserMenuQM>().field_Private_IUser_0.prop_String_0;
                SendFriendRequest(userID);
            }));
            createFreezeButton.GetComponentInChildren<TextMeshProUGUI>().text = "Send Pub Key";




        }
        public void StartAcceptingFriendRequests()
        {
            Client.GetClient().RegisterEvent(RequestFriendString, AcceptFriendRequest, true);
        }

        public void StopAcceptingFriendRequests()
        {
            Client.GetClient().RemoveEvent(RequestFriendString);
        }

        public void SendFriendRequest(string userID)
        {
            Client.GetClient().Send(new Message() { Method = RequestFriendString, Target = userID });
        }

        public void AcceptFriendRequest(Message msg)
        {
            AsyncUtilsVRCWS.ToMain(() =>
            {
                VRCUiPopupManager.field_Private_Static_VRCUiPopupManager_0.Method_Public_Void_String_String_String_Action_String_Action_Action_1_VRCUiPopup_0(
                "Accept PubKey", $"Accept Friend Request from user {msg.Target}", "Decline", new Action(() => { VRCUiManager.prop_VRCUiManager_0.HideScreen("POPUP"); }), "Accept",new Action( () => {  VRCUiManager.prop_VRCUiManager_0.HideScreen("POPUP"); }));
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
        public void RegisterEvent(string method, MessageEvent e, bool worldOnly = true)
        {
            lock (Methods)
            {
                MelonLogger.Msg($"Registering Event {method}");
                AcceptedMethod acceptedMethod = new AcceptedMethod() { Method = method, WorldOnly = worldOnly };
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
            Send(new Message() { Method = AcceptMethodString, Content = JsonConvert.SerializeObject(method) });
        }
        private void RemoveMethod(AcceptedMethod method)
        {
            Send(new Message() { Method = RemoveMethodString, Content = JsonConvert.SerializeObject(method) });
        }

        public void IsUserOnline(string userID)
        {
            Send(new Message() { Method = IsOnlineString, Target = userID });
        }

        public async Task<bool> IsUserOnlineAsyncResponse(string userID)
        {
            var response = await SendWithResponse(new Message() { Method = IsOnlineString, Target = userID });

            return response.Content == OnlineString;

        }

        public void DoesUserAcceptMethod(string userID, string method)
        {
            Send(new Message() { Method = DoesUserAcceptMethodString, Target = userID, Content = method });
        }
        public async Task<bool> DoesUserAcceptMethodAsyncResponse(string userID, string method)
        {
            var response = await SendWithResponse(new Message() { Method = DoesUserAcceptMethodString, Target = userID, Content = method });

            return response.Method == MethodIsAcceptedString;

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
