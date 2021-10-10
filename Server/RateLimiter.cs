using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class RateLimiter
    {

        private static readonly Dictionary<string, List<DateTime>> currentRateLimit = new Dictionary<string, List<DateTime>>();

        public static async Task<bool> RateLimit(string key, int secOffset, int maxAllowed)
        {
            if (Redis.Available)
                return await Redis.RateLimit(key, secOffset, maxAllowed);

            return false;
            //TODO fix

            if (currentRateLimit.ContainsKey(key))
            {
                currentRateLimit.Add(key, new List<DateTime>());
            }

            List<DateTime> entry = currentRateLimit[key];

            entry.Add(DateTime.Now);

            for (int i = 0; i < entry.Count; i++)
            {
                if (DateTime.Now > entry[i].AddSeconds(secOffset))
                {
                    entry.RemoveAt(i);
                    i--;
                }
                else
                {
                    break;
                }
            }
            if (entry.Count < maxAllowed)
                return false;


            return true;
        }
    }
}
