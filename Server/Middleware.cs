using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;

namespace Server
{
    public abstract class Middleware
    {
        public Middleware next;
        public void SetNext(Middleware middleware)
        {
            next = middleware;
        }

        public abstract Task Process(VRCWS userVRCWS, MessageEventArgs e, Message msg);

        public async Task CallNext(VRCWS userVRCWS, MessageEventArgs e, Message msg)
        {
            await next?.Process(userVRCWS, e, msg);
        }

    }


    public class MiddleWareManager
    {
        public Middleware firstMiddleware;
        public Middleware lastMiddleware;

        public MiddleWareManager AddMiddleWare(Middleware middleware)
        {
            if(firstMiddleware == null)
                firstMiddleware = lastMiddleware = middleware;

            lastMiddleware.SetNext(middleware);
            lastMiddleware = middleware;

            return this;
        }

        public async Task Execute(VRCWS userVRCWS, MessageEventArgs e) => await firstMiddleware?.Process(userVRCWS, e, null);


    }

}
