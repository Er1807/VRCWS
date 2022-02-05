using Newtonsoft.Json;
using Prometheus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
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

        protected override async void OnOpen()
        {
            await Redis.Increase("Connected");
        }

        protected override async void OnMessage(MessageEventArgs e)
        {
            Message msg = null;
            try
            {
                if (!await BasicValidate(e))
                    return;

                try
                {
                    msg = JsonConvert.DeserializeObject<Message>(e.Data);
                }
                catch (Exception)
                {
                    await Send(new Message() { Method = ErrorString, Content = InvalidMessageString });
                    Program.RecievedMessages.WithLabels("Invalid").Inc();
                    await Redis.Increase("Invalid");
                    return;
                }
                Program.RecievedMessages.WithLabels(msg.Method).Inc();
                await Redis.Increase("RecievedMessages");
                await Redis.Increase($"RecievedMessage:{msg.Method}");
                Console.WriteLine($"<< {userID}: {msg}");


                if (await HandleAuthentification(msg))
                    return;

                if (!authenticated)
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = LoginFirstString });
                    return;
                }

                if (await HandleFriends(msg))
                    return;


                if (await HandleBasicCommands(msg))
                    return;

                if(! await Redis.IsFriend(userID, msg.Target))
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = NoFriendsString });
                    return;
                }

                if (msg.Method == IsOnlineString)
                {
                    if (userIDToVRCWS.ContainsKey(msg.Target))
                    {
                        await Send(new Message(msg) { Method = OnlineStatusString, Target = msg.Target, Content = OnlineString });
                    }
                    else
                    {
                        await Send(new Message(msg) { Method = OnlineStatusString, Target = msg.Target, Content = OfflineString });
                    }
                }
                else if (msg.Method == DoesUserAcceptMethodString)
                {
                    msg.Method = msg.Content; // remap
                    if (ProxyRequestValid(msg))
                    {
                        await Send(new Message(msg) { Method = MethodIsAcceptedString, Target = msg.Target, Content = msg.Content });
                    }
                    else
                    {
                        await Send(new Message(msg) { Method = MethodIsDeclined, Target = msg.Target, Content = msg.Content });
                    }
                }
                else
                {
                    await ProxyMessage(msg);
                }
            }
            catch (Exception ex)
            {
                Redis.LogError(ex, msg, userID, world);
            }
        }

        private async Task<bool> HandleAuthentification(Message msg)
        {
            if (msg.Method == RegisterString)
            {
                userID = msg.Target;
                randomText = Guid.NewGuid().ToString();
                await Send(new Message(msg) { Method = RegisterChallengeString, Content = randomText });
                return true;
            }

            if (msg.Method == RegisterChallengeCompletedString)
            {
                userID = randomText;
                await Task.Delay(5000);
                if (await VrcApi.QueueGetUserBio(userID) == randomText)
                {
                    X509Certificate2 certificate = CertificateValidation.SignCSR(msg.Content, userID);


                    await Send(new Message(msg) { Method = RegisterChallengeSuccessString, Content = Convert.ToBase64String(certificate.RawData) });
                }
                else
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = RegisterChallengeFailedString });
                }
                return true;
            }

            if (msg.Method == LoginString)
            {
                userID = msg.Target;
                LoginMessage loginMessage = msg.GetContentAs<LoginMessage>();
                if(loginMessage == null)
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = NoCertificateProvidedString });
                    return true;
                }
                if (userIDToVRCWS.ContainsKey(userID))
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = AlreadyConnectedString });
                    return true;
                }
                X509Certificate2 cert = new X509Certificate2(Convert.FromBase64String(loginMessage.Certificate));

                if(!CertificateValidation.ValidateCertificateAgainstRoot(cert, userID, Convert.FromBase64String(loginMessage.Signature)))
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = InvalidCertificateString });
                    return true;
                }
                authenticated = true;
                userIDToVRCWS[userID] = this;
                await Send(new Message(msg) { Method = ConnectedString });
                await Redis.Increase("UniqueConnected", userID);
                UpdateStats();
                return true;
            }
            return false;
        }

        private async Task<bool> HandleFriends(Message msg)
        {
            if (msg.Method == RequestFriendString)
            {
                if (msg.Target != null && userIDToVRCWS.ContainsKey(msg.Target) && userIDToVRCWS[msg.Target].acceptableMethods.Any(x=>x.Method == RequestFriendString))
                {
                    await Redis.AddFriendRequest(userID, msg.Target);
                    await userIDToVRCWS[msg.Target].Send(new Message(msg) { Target = userID });
                }
                return true;
            }

            if (msg.Method == AddFriendString)
            {
                if (await Redis.HasFriendRequest(msg.Target, userID))
                {
                    await Redis.RemoveFriendRequest(msg.Target, userID);
                    await Redis.AddFriend(userID, msg.Target);
                }
                return true;
            }

            if (msg.Method == RemoveFriendString)
            {
                await Redis.RemoveFriend(userID, msg.Target);
                return true;
            }

            if (msg.Method == GetFriendsString)
            {
                await Send(new Message(msg) { Content = JsonConvert.SerializeObject(Redis.GetFriends(userID)) });
                return true;
            }

            return false;
        }

        private async Task<bool> HandleBasicCommands(Message msg)
        {
            if (msg.Method == SetWorldString)
            {
                world = msg.Content;
                await Send(new Message(msg) { Method = WorldUpdatedString });
                return true;
            }

            if (msg.Method == AcceptMethodString)
            {
                var acceptedMethod = msg.GetContentAs<AcceptedMethod>();
                if (acceptedMethod == null)
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = InvalidMessageString });
                    return true;
                }
                if (acceptableMethods.Count > 1024)
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = ToManyMethodsString });
                    return true;
                }
                if (acceptableMethods.Any(x => x.Method == acceptedMethod.Method))
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = MethodAlreadyExistedString });
                    return true;
                }

                acceptableMethods.Add(acceptedMethod);
                await Send(new Message(msg) { Method = MethodsUpdatedString });
                return true;

            }

            if (msg.Method == RemoveMethodString)
            {
                var acceptedMethod = msg.GetContentAs<AcceptedMethod>();
                if (acceptedMethod == null)
                {
                    await Send(new Message(msg) { Method = ErrorString, Content = InvalidMessageString });
                    return true;
                }
                var item = acceptableMethods.FirstOrDefault(x => x.Method == acceptedMethod.Method);
                acceptableMethods.Remove(item);
                await Send(new Message(msg) { Method = MethodsUpdatedString });

                return true;
            }
            return false;
        }

        private async Task<bool> BasicValidate(MessageEventArgs e)
        {
            if (!e.IsText)
            {
                await Send(new Message() { Method = ErrorString, Content = InvalidMessageString });
                Program.RecievedMessages.WithLabels("Invalid").Inc();
                await Redis.Increase("Invalid");
                return false;
            }
            if (await RateLimiter.RateLimit("message:" + ID, 5, 40))
            {
                await Send(new Message() { Method = ErrorString, Content = RatelimitedString });
                Program.RecievedMessages.WithLabels("Invalid").Inc();
                await Redis.Increase("Ratelimited");
                Program.RateLimits.Inc();
                return false;
            }
            if (e.RawData.Length > 5120)//5kb
            {
                await Send(new Message() { Method = ErrorString, Content = MessageToLargeString });
                Program.RecievedMessages.WithLabels("Invalid").Inc();
                await Redis.Increase("Messagetolarge");
                return false;
            }
            return true;
        }

        private async Task ProxyMessage(Message msg)
        {
            Program.ProxyMessagesAttempt.WithLabels(msg.Method).Inc();
            await Redis.Increase("ProxyMessagesAttempt");
            await Redis.Increase($"ProxyMessagesAttempt:{msg.Method}");


            if (msg.Target == null)
            {
                await Send(new Message(msg) { Method = ErrorString, Content = NoTargetProvidedString });
                return;
            }

            if (!userIDToVRCWS.ContainsKey(msg.Target))
            {
                await Send(new Message(msg) { Method = ErrorString, Target = msg.Target, Content = UserOfflineString });
                return;
            }
            var remoteUser = userIDToVRCWS[msg.Target];
            if (!ProxyRequestValid(msg))
            {
                await Send(new Message(msg) { Method = ErrorString, Target = msg.Target, Content = MethodNotAceptedString });
                return;
            }
            msg.Target = userID;
            await remoteUser.Send(msg);
            Program.ProxyMessages.WithLabels(msg.Method).Inc();
            await Redis.Increase("ProxyMessages");
            await Redis.Increase($"ProxyMessage:{msg.Method}");
        }

        private bool ProxyRequestValid(Message msg)
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
            Task.Run(ReportTask);

            exitEvent.WaitOne();
            wssv.Stop();
            server.Stop();

        }

        private static void CrashHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;

            Redis.LogError(e, null, null, null);
        }

        private static void ReportTask()
        {
            while (true)
            {
                try
                {
                    Dictionary<string, int> usersPerMethod = new Dictionary<string, int>();
                    foreach (var item in VRCWS.userIDToVRCWS.ToArray())//clone to mitigate errors
                    {
                        foreach (var item2 in item.Value.acceptableMethods.ToArray())//clone to mitigate errors
                        {
                            usersPerMethod[item2.Method] = usersPerMethod.GetValueOrDefault(item2.Method, 0) + 1;
                        }
                    }

                    foreach (var item in usersPerMethod)
                    {
                        ActiveMethods.WithLabels(item.Key).Set(item.Value);
                    }
                    foreach (var item in ActiveMethods.GetAllLabelValues().Select(x => x[0]).Where(x => !usersPerMethod.ContainsKey(x)))
                    {
                        ActiveMethods.WithLabels(item).Remove();
                    };
                }
                catch (Exception)
                {
                    Console.WriteLine("[ReportTask] error occured");
                }
                Thread.Sleep(1000 * 3);
            }
        }
    }
}
