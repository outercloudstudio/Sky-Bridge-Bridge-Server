using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SkyBridge
{
    class SkyBridge
    {
        public static int bufferSize = 4096;
        public static int bytesPerSecond = bufferSize * 60;
        public static float timeout = 30;
        public static float keepalive = 5;
    }
}
