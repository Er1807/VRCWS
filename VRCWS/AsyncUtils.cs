using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VRCWSLibary
{
    // Based on https://github.com/loukylor/VRC-Mods/blob/main/VRChatUtilityKit/Utilities/AsyncUtils.cs
    // By loukylor
    // original by knah
    public static class AsyncUtils
    {
        internal static System.Collections.Concurrent.ConcurrentQueue<Action> _toMainThreadQueue = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public static void ToMain(Action action)
        {
            _toMainThreadQueue.Enqueue(action);
        }

    }
}
