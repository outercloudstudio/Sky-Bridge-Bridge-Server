using System;

namespace BridgeServer
{
    [Serializable]
    public class Room
    {
        public string name;
        public string ID;

        public string password;

        public Connection hostConnection;
    }
}
