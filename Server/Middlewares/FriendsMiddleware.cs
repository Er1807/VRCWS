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
    class FriendsMiddleware : Middleware
    {
        public override async Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {

            if (msg.Method == RequestFriendString)
            {
                if (msg.Target != null && VRCWS.userIDToVRCWS.ContainsKey(msg.Target) && VRCWS.userIDToVRCWS[msg.Target].acceptableMethods.Any(x => x.Method == RequestFriendString))
                {
                    await Redis.AddFriendRequest(userVRCWS.userID, msg.Target);
                    await VRCWS.userIDToVRCWS[msg.Target].Send(new Message(msg) { Target = userVRCWS.userID });
                }
                return;
            }

            if (msg.Method == AddFriendString)
            {
                if (await Redis.HasFriendRequest(msg.Target, userVRCWS.userID))
                {
                    await Redis.RemoveFriendRequest(msg.Target, userVRCWS.userID);
                    await Redis.AddFriend(userVRCWS.userID, msg.Target);
                }
                return;
            }

            if (msg.Method == RemoveFriendString)
            {
                await Redis.RemoveFriend(userVRCWS.userID, msg.Target);
                return;
            }

            if (msg.Method == GetFriendsString)
            {
                await userVRCWS.Send(new Message(msg) { Content = JsonConvert.SerializeObject(Redis.GetFriends(userVRCWS.userID)) });
                return;
            }

            if (!await Redis.IsFriend(userVRCWS.userID, msg.Target))
            {
                await userVRCWS.Send(new Message(msg) { Method = ErrorString, Content = NoFriendsString });
                return;
            }

            await CallNext(userVRCWS, e, msg);
        }
    }
}
