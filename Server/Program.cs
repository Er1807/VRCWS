using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Server
{
    public class VRCWS : WebSocketBehavior
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

        public static Dictionary<string, VRCWS> userIDToVRCWS = new Dictionary<string, VRCWS>();

        public string userID;
        public string world;

        public List<Tuple<string,bool>> acceptableMethods = new List<Tuple<string, bool>>();

        protected override void OnMessage(MessageEventArgs e)
        {
            Message msg =  JsonConvert.DeserializeObject<Message>(e.Data);
            Console.WriteLine($"<< {msg}");
            if(msg.Method == "StartConnection")
            {
                if (userIDToVRCWS.ContainsKey(msg.Content)){
                    Send(new Message() { Method = "Error", Content = "AlreadyConnected" });
                    return;
                }
                userID = msg.Content;
                userIDToVRCWS[userID] = this;
                Send(new Message() { Method = "Connected" });
                return;
            }

            if (userID == null)
            {
                Send(new Message() { Method = "Error", Content = "StartConnectionFirst" });
                return;
            }
            if (msg.Method == "SetWorld")
            {
                world = msg.Content;
                Send(new Message() { Method = "MethodsUpdated" });
            }
            else if (msg.Method == "AcceptMethod")
            {
                var arr = msg.Content.Split(";");
                acceptableMethods.Add(new Tuple<string, bool>(arr[0], arr[1] == "true"));
                Send(new Message() { Method = "MethodsUpdated" });
            }
            else if(msg.Method == "RemoveMethod")
            {
                var item = acceptableMethods.FirstOrDefault(x => x.Item1 == msg.Content);
                acceptableMethods.Remove(item);
                Send(new Message() { Method = "MethodsUpdated"});
            }
            else if (msg.Method == "IsOnline")
            {
                if (userIDToVRCWS.ContainsKey(msg.Target)){
                    Send(new Message() { Method = "OnlineStatus", Target = msg.Target, Content = "Online" });
                }
                else
                {
                    Send(new Message() { Method = "OnlineStatus", Target = msg.Target, Content = "Offline" });
                }
            } 
            else
            {
                if (!userIDToVRCWS.ContainsKey(msg.Target)) {
                    Send(new Message() { Method = "Error", Target = msg.Target, Content = "UserOffline" });
                    return;
                }
                var remoteUser = userIDToVRCWS[msg.Target];
                var item = remoteUser.acceptableMethods.First(x => x.Item1 == msg.Content);
                if(item==null || item.Item2 && world != remoteUser.world)
                {
                    Send(new Message() { Method = "Error", Target = msg.Target, Content = "MethodNotAcepted" });
                    return;
                }
                msg.Target = userID;
                remoteUser.Send(msg);
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (userID == null) return;
            Console.WriteLine($"User {userID} dissconected");
                userIDToVRCWS.Remove(userID);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            if (userID == null) return;
            Console.WriteLine($"User {userID} errored");
            userIDToVRCWS.Remove(userID);
        }

        public void Send(Message msg)
        {
            Console.WriteLine($">> {msg}");
            Send(JsonConvert.SerializeObject(msg));
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {

            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eventArgs) => {
                eventArgs.Cancel = true;
                exitEvent.Set();
            };

            var wssv = new WebSocketServer("ws://0.0.0.0:8080");
            wssv.AddWebSocketService<VRCWS>("/VRC");
            wssv.AllowForwardedRequest = true;
            
            wssv.Start();
            Console.WriteLine("Listening");
            exitEvent.WaitOne();
            wssv.Stop();
        }
    }
}
