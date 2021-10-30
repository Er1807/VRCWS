using MelonLoader;
using Newtonsoft.Json;
using System.Collections.Generic;
using VRCWSLibary;
using HarmonyLib;
using VRChatUtilityKit.Utilities;
using VRChatUtilityKit.Ui;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.UI;
using UnhollowerRuntimeLib;
using UnityEngine.Events;
using System;

[assembly: MelonInfo(typeof(VRCWSLibaryMod), "VRCWSLibary", "1.0.13", "Eric van Fandenfart")]
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
                if (newValue) Client.GetClient().connection.Connect(entryURL.Value);
                else Client.GetClient().connection.Disconnect();
            };


            if (entryConnect.Value)
                Client.GetClient().connection.Connect(entryURL.Value);


            MelonPreferences_Entry<bool> entrypubKey = category.CreateEntry("Accept Public Key", false);
            //entrypubKey.Value = false;//force value to false on startup
            entrypubKey.Save();
            entrypubKey.OnValueChanged += (oldValue, newValue) => {
                if (newValue) StartAcceptingKey();
                else StopAcceptingKeys();
            };

            HarmonyInstance.Patch(typeof(NetworkManager).GetMethod("OnJoinedRoom"), new HarmonyMethod(typeof(Client).GetMethod("OnJoinedRoom")));
            VRCUtils.OnUiManagerInit += Init;
        }

        private void Init()
        {
            var baseUIElement = GameObject.Find("UserInterface/MenuContent/Screens/UserInfo/Buttons/RightSideButtons/RightUpperButtonColumn/PlaylistsButton").gameObject;

            var gameObject = GameObject.Instantiate(baseUIElement, baseUIElement.transform.parent, true);
            gameObject.name = "Send_PubKey";

            var uitext = gameObject.GetComponentInChildren<Text>();
            uitext.text = "Send Pub Key";

            var button = gameObject.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            var action = new Action(delegate (){
                MelonLogger.Msg($"Sending public key");
                string userID = VRCUtils.ActiveUserInUserInfoMenu.id;
                SendPubKey(userID);
            });
            button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(action));


            MelonLogger.Msg("Buttons sucessfully created");
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
            Task.Run(async () => {
                await AsyncUtils.YieldToMainThread();
                UiManager.OpenPopup("Accept PubKey", $"Accept Public key from user {msg.Target}", "Decline", () => { UiManager.ClosePopup(); }, "Accept", () => { SecurityContext.AcceptPubKey(msg.Content, msg.Target); UiManager.ClosePopup(); });
            });
            
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
                foreach (var item in Methods.Keys)
                {
                    AcceptMethod(item);
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
            MelonLogger.Msg($"Registering Event {method}");
            AcceptedMethod acceptedMethod = new AcceptedMethod() {Method = method, WorldOnly = worldOnly, SignatureRequired = signatureRequired };
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

        public void RemoveEvent(string method)
        {
            AcceptedMethod acceptedMethod = new AcceptedMethod() { Method = method};
            Methods.Remove(acceptedMethod);
            RemoveMethod(acceptedMethod);
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
        public void DoesUserAcceptMethod(string userID, string method)
        {
            Send(new Message() { Method = "DoesUserAcceptMethod", Target = userID, Content=method });
        }

        public void Send(Message msg)
        {
            connection.Send(msg);
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
