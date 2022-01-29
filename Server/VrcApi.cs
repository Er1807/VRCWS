using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;

namespace Server
{
    class VrcApi
    {
        private static UsersApi UserApi;
        static VrcApi()
        {
            // Authentication credentials
            Configuration config = new Configuration();
            config.Username = Environment.GetEnvironmentVariable("VRCUSER");
            config.Password = Environment.GetEnvironmentVariable("VRCPASSWORD");

            // Create instances of API's we'll need
            AuthenticationApi authApi = new AuthenticationApi(config);
            UserApi = new UsersApi(config);

            authApi.GetCurrentUser();


            new Thread(Runner).Start();

        }

        private static ConcurrentQueue<(TaskCompletionSource<string>, string)> queue = new();

        private static void Runner()
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

        private static async Task<string> GetUserBio(string userID)
        {

            User user = await UserApi.GetUserAsync(userID);

            return user.Bio;
        }
    }
}
