using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;

namespace Server.Middlewares
{
    class TryCatchMiddleware : Middleware
    {
        public override async Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {
            try
            {
                await CallNext(userVRCWS, e, msg);
            }
            catch (Exception ex)
            {
                Redis.LogError(ex, msg, userVRCWS.userID, userVRCWS.world);
            }
        }
    }
}
