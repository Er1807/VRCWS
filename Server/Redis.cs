using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public static async Task Increase(string key, string value = null)
        {
            if (!Available)
                return;
            if (value == null)
                await db.StringIncrementAsync("Inc:" + key);
            else
                await db.SetAddAsync(key, value);
        }

        public static void LogError(Exception exception, Message message, string userID, string world)
        {
            if (!Available)
                return;
            string time = DateTime.Now.Ticks / 10000000 + "";
            var expire = new TimeSpan(5, 0, 0, 0);
            db.StringSet($"Exception:{time}:Message", exception.Message , expire);
            db.StringSet($"Exception:{time}:StackTrace", exception.StackTrace, expire);
            if (exception.InnerException != null)
            {
                db.StringSet($"Exception:{time}:InnerMessage", exception.InnerException.Message, expire);
                db.StringSet($"Exception:{time}:InnerStackTrace", exception.InnerException.StackTrace, expire);
            }
            if (message != null)
                db.StringSet($"Exception:{time}:SendMessage", JsonConvert.SerializeObject(message), expire);
            
            if (userID != null)
                db.StringSet($"Exception:{time}:UserID", userID, expire);

            if (world != null)
                db.StringSet($"Exception:{time}:World", world, expire);
        }

        public static async Task Set(string key, string value)
        {
            if (!Available)
                return;
            await db.StringSetAsync(key, value);
        }

        public static async Task<string> Get(string key)
        {
            if (!Available)
                return null;
            return await db.StringGetAsync(key);
        }

        public static async Task AddFriend(string userID1, string userID2)
        {
            await db.SetAddAsync($"Friends:{userID1}", userID2);
            await db.SetAddAsync($"Friends:{userID2}", userID1);
        }

        public static async Task<List<string>> GetFriends(string userID1)
        {
           return (await db.SetMembersAsync($"Friends:{userID1}")).Select(x=>x.ToString()).ToList();
        }

        public static async Task RemoveFriend(string userID1, string userID2)
        {

            await db.SetRemoveAsync($"Friends:{userID1}", userID2);
            await db.SetRemoveAsync($"Friends:{userID2}", userID1);
        }
        public static async Task<bool> IsFriend(string userID1, string userID2)
        {
            return await db.SetContainsAsync($"Friends:{userID1}", userID2);
        }

        public static async Task AddFriendRequest(string userID1, string userID2)
        {
            await db.SetAddAsync($"FriendRequests:{userID1}", userID2);
        }

        public static async Task RemoveFriendRequest(string userID1, string userID2)
        {
            await db.SetRemoveAsync($"FriendRequests:{userID1}", userID2);
        }

        public static async Task<bool> HasFriendRequest(string userID1, string userID2)
        {
            return await db.SetContainsAsync($"FriendRequests:{userID1}", userID2);
        }

    }
}
