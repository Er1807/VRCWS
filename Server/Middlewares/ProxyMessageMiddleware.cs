using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using static Server.Strings;

namespace Server.Middlewares
{
    class ProxyMessageMiddleware : Middleware
    {
        public override async Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {
            Program.ProxyMessagesAttempt.WithLabels(msg.Method).Inc();
            await Redis.Increase("ProxyMessagesAttempt");
            await Redis.Increase($"ProxyMessagesAttempt:{msg.Method}");


            if (msg.Target == null)
            {
                await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = NoTargetProvidedString });
                return;
            }

            if (!VRCWS.userIDToVRCWS.ContainsKey(msg.Target))
            {
                await userVRCWS.Send(new Message(msg) { Method = ErrorString, Target = msg.Target, Content = UserOfflineString });
                return;
            }
            var remoteUser = VRCWS.userIDToVRCWS[msg.Target];
            if (!userVRCWS.ProxyRequestValid(msg))
            {
                await userVRCWS.Send(new Message(msg) { Method = ErrorString, Target = msg.Target, Content = MethodNotAceptedString });
                return;
            }
            msg.Target = userVRCWS.userID;
            await remoteUser.Send(msg);
            Program.ProxyMessages.WithLabels(msg.Method).Inc();
            await Redis.Increase("ProxyMessages");
            await Redis.Increase($"ProxyMessage:{msg.Method}");
        }
    }
}
