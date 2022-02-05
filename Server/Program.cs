using Newtonsoft.Json;
using Prometheus;
using Server.Middlewares;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using static Server.Strings;

namespace Server
{
    public class VRCWS : WebSocketBehavior
    {


        public static Dictionary<string, VRCWS> userIDToVRCWS = new Dictionary<string, VRCWS>();

        public string userID;
        public string world;
        public bool authenticated = false;
        public string randomText = "";

        public List<AcceptedMethod> acceptableMethods = new List<AcceptedMethod>();

        public static MiddleWareManager Manager { get; internal set; }

        protected override async void OnOpen()
        {
            await Redis.Increase("Connected");
        }

        protected override async void OnMessage(MessageEventArgs e)
        {
            Manager.Execute(this, e);
        }




        public bool ProxyRequestValid(Message msg)
        {
            if (msg.Target == null || !userIDToVRCWS.ContainsKey(msg.Target))
            {
                return false;
            }

            var remoteUser = userIDToVRCWS[msg.Target];
            var item = remoteUser.acceptableMethods.FirstOrDefault(x => x.Method == msg.Method);
            if (item == null || item.WorldOnly && world != remoteUser.world)
            {
                return false;
            }

            return true;
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (userID == null) return;
            Console.WriteLine($"User {userID} dissconected");
            userIDToVRCWS.Remove(userID);
            UpdateStats();
        }

        protected override void OnError(WebSocketSharp.ErrorEventArgs e)
        {
            if (userID == null) return;
            Console.WriteLine($"User {userID} errored");
            userIDToVRCWS.Remove(userID);
            UpdateStats();
        }

        public async Task Send(Message msg)
        {
            Program.SendMessages.WithLabels(msg.Method).Inc();
            await Redis.Increase($"SendMessages");
            await Redis.Increase($"SendMessage:{msg.Method}");
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
        public static readonly Gauge RateLimits = Metrics.CreateGauge("vrcws_ratelimit_hit", "Rate Limit hits");
        public static readonly Gauge CurrentUsers = Metrics.CreateGauge("vrcws_active_users_current", "Active users");
        public static readonly Counter SendMessages = Metrics.CreateCounter("vrcws_send_messages", "Messages send", new CounterConfiguration { LabelNames = new[] { "method" } });
        public static readonly Counter RecievedMessages = Metrics.CreateCounter("vrcws_recieved_messages", "Messages recieved", new CounterConfiguration { LabelNames = new[] { "method" } });
        public static readonly Counter ProxyMessages = Metrics.CreateCounter("vrcws_proxy_messages", "Messages proxied", new CounterConfiguration { LabelNames = new[] { "method" } });
        public static readonly Counter ProxyMessagesAttempt = Metrics.CreateCounter("vrcws_proxy_messages_attempt", "Messages proxied attempt", new CounterConfiguration { LabelNames = new[] { "method" } });
        public static readonly Gauge ActiveMethods = Metrics.CreateGauge("vrcws_active_users_per_method", "Active Methods per User", new GaugeConfiguration { LabelNames = new[] { "method" } });
        public static readonly Gauge VeryfyQueue = Metrics.CreateGauge("vrcws_verifyqueue", "Verify Queue");

        public static void Main(string[] args)
        {
            MiddleWareManager manager = new MiddleWareManager();
            manager.AddMiddleWare(new TryCatchMiddleware());
            manager.AddMiddleWare(new ParserMiddleWare());
            manager.AddMiddleWare(new AuthentificationMiddleware());
            manager.AddMiddleWare(new BasicCommandsMiddleware());
            manager.AddMiddleWare(new FriendsMiddleware());
            manager.AddMiddleWare(new OtherPersonMidleware());
            manager.AddMiddleWare(new ProxyMessageMiddleware());

            VRCWS.Manager = manager;

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                exitEvent.Set();
            };
            var wssv = new WebSocketServer("ws://0.0.0.0:8080");

            wssv.Log.Output = (_, __) => { }; // disable log
            var server = new MetricServer(9100);
            wssv.AddWebSocketService<VRCWS>("/VRC");
            wssv.AllowForwardedRequest = true;

            Console.WriteLine("Starting Metric service");
            server.Start();
            Console.WriteLine("Starting WS Server");
            wssv.Start();
            Console.WriteLine("Listening");

            Console.WriteLine("Starting Cleanup Task");

            exitEvent.WaitOne();
            wssv.Stop();
            server.Stop();

        }

        private static void CrashHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;

            Redis.LogError(e, null, null, null);
        }

        
    }
}
