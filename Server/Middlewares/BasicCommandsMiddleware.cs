using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using static Server.Strings;

namespace Server.Middlewares
{
    class BasicCommandsMiddleware : Middleware
    {
        public override async Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {
            if (msg.Method == SetWorldString)
            {
                userVRCWS.world = msg.Content;
                await userVRCWS.Send(new Message(msg) { Method = WorldUpdatedString });
                return;
            }

            if (msg.Method == AcceptMethodString)
            {
                var acceptedMethod = msg.GetContentAs<AcceptedMethod>();
                if (acceptedMethod == null)
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = InvalidMessageString });
                    return;
                }
                if (userVRCWS.acceptableMethods.Count > 1024)
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = ToManyMethodsString });
                    return;
                }
                if (userVRCWS.acceptableMethods.Any(x => x.Method == acceptedMethod.Method))
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = MethodAlreadyExistedString });
                    return;
                }

                userVRCWS.acceptableMethods.Add(acceptedMethod);
                await userVRCWS.Send(new Message(msg) { Method = MethodsUpdatedString });
                return;

            }

            if (msg.Method == RemoveMethodString)
            {
                var acceptedMethod = msg.GetContentAs<AcceptedMethod>();
                if (acceptedMethod == null)
                {
                    await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = InvalidMessageString });
                    return ;
                }
                var item = userVRCWS.acceptableMethods.FirstOrDefault(x => x.Method == acceptedMethod.Method);
                userVRCWS.acceptableMethods.Remove(item);
                await userVRCWS.Send(new Message(msg) { Method = MethodsUpdatedString });

                return;
            }

            await CallNext(userVRCWS, e, msg);
        }
    }
}
