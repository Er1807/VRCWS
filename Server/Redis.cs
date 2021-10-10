using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class Redis
    {
        private static ConnectionMultiplexer conn;
        private static IDatabase db;
        public static bool Available => db != null;

        static Redis() {
            try
            {
                ConfigurationOptions options = new ConfigurationOptions();
                options.ConnectTimeout = 2000;
                options.ConnectRetry = 5;
                options.EndPoints.Add(Environment.GetEnvironmentVariable("REDIS") ?? "localhost");
                Console.WriteLine(Environment.GetEnvironmentVariable("REDIS"));
                conn = ConnectionMultiplexer.Connect(options);
                db = conn.GetDatabase();
                Console.WriteLine("Connection to Redis established");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine("Connection to Redis not available");
                //not available
            }
          
        }

        public static async Task<bool> RateLimit(string key, int secOffset, int maxAllowed)
        {
            key += ":"+(DateTime.Now.Ticks / 10000000)/secOffset;

            var transaction = db.CreateTransaction();
            
            var result = transaction.StringIncrementAsync(key);
            transaction.KeyExpireAsync(key, DateTime.Now.AddSeconds(60));
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

    }
}
