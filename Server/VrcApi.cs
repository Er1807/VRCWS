using RestSharp;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RestSharp.Authenticators;
using Newtonsoft.Json.Linq;

namespace Server
{
    class VrcApi
    {

        private static ConcurrentQueue<(TaskCompletionSource<string>, string)> queue = new();
        private RestClient client;

        static VrcApi()
        {
            try
            {
                var users = Redis.GetHashSet("VerificationUsers").Result;

                foreach (var entry in users)
                {
                    new VrcApi(entry);
                }
            }
            catch (Exception ex)
            {

                throw;
            }
            
        }

        public VrcApi(HashEntry entry)
        {
            client = new RestClient(new RestClientOptions() { UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.87 Safari/537.36" });

            client.CookieContainer.Add(new Cookie("apiKey", "JlE5Jldo5Jibnk5O5hTx6XVqsJu4WJ26", "/", "api.vrchat.cloud"));


            if (!string.IsNullOrEmpty(entry.Value))
                client.CookieContainer.Add(new Cookie("auth", entry.Value, "/", "api.vrchat.cloud"));


            string[] userpw = entry.Name.ToString().Split("<sep>");

            client.Authenticator = new HttpBasicAuthenticator(userpw[0], userpw[1]);


            var request = new RestRequest("https://api.vrchat.cloud/api/1/auth/user");

            RestResponse response = client.ExecuteAsync(request).Result;

            client.Authenticator = null;
            if (!response.IsSuccessful)
            {
                Console.WriteLine("Not succesfull");
                Redis.AddHash("VerificationUsers", entry.Name, "").Wait();
                return;
            }

            string authtoken = response.Cookies.FirstOrDefault(x => x.Name == "auth").Value;

            Redis.AddHash("VerificationUsers", entry.Name, authtoken ?? "").Wait();

            new Thread(Runner).Start();

        }


        private void Runner()
        {
            while (true)
            {
                if(queue.TryDequeue(out var task)){
                    string result = GetUserBio(task.Item2).Result;

                    task.Item1.SetResult(result);
                }
                Program.VeryfyQueue.Set(queue.Count);
                Thread.Sleep(5000);
            }
        }

        public static async Task<string> QueueGetUserBio(string userID)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
            queue.Enqueue((tcs, userID));

            return await tcs.Task;
            
        }

        private async Task<string> GetUserBio(string userID)
        {

            var request = new RestRequest($"https://api.vrchat.cloud/api/1/users/{userID}");
            RestResponse response = await client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                return "";
            }

            JObject userDetails = JObject.Parse(response.Content);

            return userDetails["bio"].ToString();
        }
    }
}
