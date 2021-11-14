using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    public class Redis
    {
        private static ConnectionMultiplexer conn;
        private static IDatabase db;
        public static bool Available => db != null;

        static Redis() {
            
            Task.Run(async() => {
                try
                {
                    ThreadPool.SetMinThreads(250, 250);
                    ConfigurationOptions options = new ConfigurationOptions();
                    options.ConnectTimeout = 500;
                    options.ConnectRetry = 5;
                    options.AbortOnConnectFail = false;
                    options.EndPoints.Add(Environment.GetEnvironmentVariable("REDIS") ?? "localhost");
                    Console.WriteLine(Environment.GetEnvironmentVariable("REDIS"));
                    conn = await ConnectionMultiplexer.ConnectAsync(options);

                    db = conn.GetDatabase();
                    Console.WriteLine("Connection to Redis established");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine("Connection to Redis not available");
                    //not available
                }
            });
                
            
          
        }

        public static async Task<bool> RateLimit(string key, int secOffset, int maxAllowed)
        {
            key += ":"+(DateTime.Now.Ticks / 10000000)/secOffset;

            var transaction = db.CreateTransaction();
            
            var result = transaction.StringIncrementAsync(key);
            _ = transaction.KeyExpireAsync(key, DateTime.Now.AddSeconds(60));//larger then secoffset
            transaction.Execute();

            if ((await result) < maxAllowed)
                return false;
            return true;
        }

        public static void Increase(string key, string value = null)
        {
            if (!Available)
                return;
            if (value == null)
                db.StringIncrementAsync("Inc:" + key);
            else
                db.SetAdd(key, value);

        }

        public static void LogError(Exception exception, VRCWS.Message message, string userID, string world)
        {
            if (!Available)
                return;
            string time = DateTime.Now.Ticks / 10000000 + "";
            db.StringSet($"Exception:{time}:Message", exception.Message);
            db.StringSet($"Exception:{time}:StackTrace", exception.StackTrace);
            if (exception.InnerException != null)
            {
                db.StringSet($"Exception:{time}:InnerMessage", exception.InnerException.Message);
                db.StringSet($"Exception:{time}:InnerStackTrace", exception.InnerException.StackTrace);
            }
            if (message != null)
                db.StringSet($"Exception:{time}:SendMessage", JsonConvert.SerializeObject(message));
            
            if (userID != null)
                db.StringSet($"Exception:{time}:UserID", userID);

            if (world != null)
                db.StringSet($"Exception:{time}:World", world);
            

        }

    }
}
