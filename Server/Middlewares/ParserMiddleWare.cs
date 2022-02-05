using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using static Server.Strings;

namespace Server.Middlewares
{
    class ParserMiddleWare : Middleware
    {
        public override async Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {
            if (!e.IsText)
            {
                await userVRCWS.Send(new Message() { Method = ErrorString, Content = InvalidMessageString });
                Program.RecievedMessages.WithLabels("Invalid").Inc();
                await Redis.Increase("Invalid");
                return;
            }
            if (await RateLimiter.RateLimit("message:" + userVRCWS.ID, 5, 40))
            {
                await userVRCWS.Send(new Message() { Method = ErrorString, Content = RatelimitedString });
                Program.RecievedMessages.WithLabels("Invalid").Inc();
                await Redis.Increase("Ratelimited");
                Program.RateLimits.Inc();
                return;
            }
            if (e.RawData.Length > 5120)//5kb
            {
                await userVRCWS.Send(new Message() { Method = ErrorString, Content = MessageToLargeString });
                Program.RecievedMessages.WithLabels("Invalid").Inc();
                await Redis.Increase("Messagetolarge");
                return;
            }
            try
            {
                msg = JsonConvert.DeserializeObject<Message>(e.Data);
            }
            catch (Exception)
            {
                await userVRCWS.Send(new Message() { Method = ErrorString, Content = InvalidMessageString });
                Program.RecievedMessages.WithLabels("Invalid").Inc();
                await Redis.Increase("Invalid");
                return;
            }
            Program.RecievedMessages.WithLabels(msg.Method).Inc();
            await Redis.Increase("RecievedMessages");
            await Redis.Increase($"RecievedMessage:{msg.Method}");
            Console.WriteLine($"<< {userVRCWS.userID}: {msg}");

            await CallNext(userVRCWS, e, msg);

        }

    }
}
