using Newtonsoft.Json;
using Prometheus;
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
            public string Signature { get; set; }

            public override string ToString()
            {
                return $"{Method} - {Target} - {Content}";
            }
            public T GetContentAs<T>()
            {
                return JsonConvert.DeserializeObject<T>(Content);
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

        public static Dictionary<string, VRCWS> userIDToVRCWS = new Dictionary<string, VRCWS>();

        public string userID;
        public string world;

        public List<AcceptedMethod> acceptableMethods = new List<AcceptedMethod>();

        protected override void OnOpen()
        {
            UpdateStats();
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            Program.RecievedMessages.Inc();
            Message msg;
            try
            {
                msg = JsonConvert.DeserializeObject<Message>(e.Data);
            }
            catch (Exception)
            {
                Send(new Message() { Method = "Error", Content = "Invalid Message" });
                return;
            }
            Console.WriteLine($"<< {userID}: {msg}");
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
                Send(new Message() { Method = "WorldUpdated" });
            }
            else if (msg.Method == "AcceptMethod")
            {
                var acceptedMethod = msg.GetContentAs<AcceptedMethod>();
                if (acceptableMethods.Any(x => x.Method == msg.Target))
                {
                    Send(new Message() { Method = "Error" , Content = "MethodAlreadyExisted"});
                    return;
                }
                
                acceptableMethods.Add(acceptedMethod);
                Send(new Message() { Method = "MethodsUpdated" });
                
            }
            else if(msg.Method == "RemoveMethod")
            {
                var acceptedMethod = msg.GetContentAs<AcceptedMethod>();
                var item = acceptableMethods.FirstOrDefault(x => x.Method ==acceptedMethod.Method);
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
                ProxyMessage(msg);
            }
        }

        private void ProxyMessage(Message msg)
        {
            Program.ProxyMessagesAttampt.Inc();
            if (!userIDToVRCWS.ContainsKey(msg.Target))
            {
                Send(new Message() { Method = "Error", Target = msg.Target, Content = "UserOffline" });
                return;
            }
            var remoteUser = userIDToVRCWS[msg.Target];
            var item = remoteUser.acceptableMethods.FirstOrDefault(x => x.Method == msg.Method);
            if (item == null
                || item.WorldOnly && world != remoteUser.world
                || item.SignatureRequired && String.IsNullOrWhiteSpace(msg.Signature))
            {
                Send(new Message() { Method = "Error", Target = msg.Target, Content = "MethodNotAcepted" });
                return;
            }
            msg.Target = userID;
            remoteUser.Send(msg);
            Program.ProxyMessages.Inc();
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (userID == null) return;
            Console.WriteLine($"User {userID} dissconected");
                userIDToVRCWS.Remove(userID);
            UpdateStats();
        }

        protected override void OnError(ErrorEventArgs e)
        {
            if (userID == null) return;
            Console.WriteLine($"User {userID} errored");
            userIDToVRCWS.Remove(userID);
            UpdateStats();
        }

        public void Send(Message msg)
        {
            Program.SendMessages.Inc();
            Console.WriteLine($">> {msg}");
            Send(JsonConvert.SerializeObject(msg));
        }

        public void UpdateStats()
        {
            Program.ActiveWS.Set(Sessions.Count);
            Program.CurrentUsers.Set(userIDToVRCWS.Count);
        }
    }

    public class Program
    {

        public static readonly Gauge ActiveWS = Metrics.CreateGauge("vrcws_active_ws_current", "Active web sockets");
        public static readonly Gauge CurrentUsers = Metrics.CreateGauge("vrcws_active_users_current", "Active users");
        public static readonly Counter SendMessages = Metrics.CreateCounter("vrcws_send_messages", "Messages send");
        public static readonly Counter RecievedMessages = Metrics.CreateCounter("vrcws_recieved_messages", "Messages recieved");
        public static readonly Counter ProxyMessages = Metrics.CreateCounter("vrcws_proxy_messages", "Messages proxied");
        public static readonly Counter ProxyMessagesAttampt = Metrics.CreateCounter("vrcws_proxy_messages_attempt", "Messages proxied attemt");

        public static void Main(string[] args)
        {

            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eventArgs) => {
                eventArgs.Cancel = true;
                exitEvent.Set();
            };

            var wssv = new WebSocketServer("ws://0.0.0.0:8080");
            var server = new MetricServer(9100);
            wssv.AddWebSocketService<VRCWS>("/VRC");
            wssv.AllowForwardedRequest = true;

            Console.WriteLine("Starting Metric service");
            server.Start();
            Console.WriteLine("Starting WS Server");
            wssv.Start();
            Console.WriteLine("Listening");


            exitEvent.WaitOne();
            wssv.Stop();
            server.Stop();



            

        }
    }
}
