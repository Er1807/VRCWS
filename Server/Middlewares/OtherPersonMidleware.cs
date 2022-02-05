using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using static Server.Strings;

namespace Server.Middlewares
{
    class OtherPersonMidleware : Middleware
    {
        public override async Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {
            if (msg.Method == IsOnlineString)
            {
                if (VRCWS.userIDToVRCWS.ContainsKey(msg.Target))
                {
                    await userVRCWS.Send(new Message(msg) { Method = OnlineStatusString, Target = msg.Target, Content = OnlineString });
                }
                else
                {
                    await userVRCWS.Send(new Message(msg) { Method = OnlineStatusString, Target = msg.Target, Content = OfflineString });
                }
                return;
            }
            
            if (msg.Method == DoesUserAcceptMethodString)
            {
                msg.Method = msg.Content; // remap
                if (userVRCWS.ProxyRequestValid(msg))
                {
                    await userVRCWS.Send(new Message(msg) { Method = MethodIsAcceptedString, Target = msg.Target, Content = msg.Content });
                }
                else
                {
                    await userVRCWS.Send(new Message(msg) { Method = MethodIsDeclined, Target = msg.Target, Content = msg.Content });
                }
                return;
            }

            await CallNext(userVRCWS, e, msg);
        }
    }
}
