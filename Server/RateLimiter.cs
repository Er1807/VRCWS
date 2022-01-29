using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class RateLimiter
    {

        public static async Task<bool> RateLimit(string key, int secOffset, int maxAllowed)
        {
            if (Redis.Available)
                return await Redis.RateLimit(key, secOffset, maxAllowed);

            return false;
        }
    }
}
