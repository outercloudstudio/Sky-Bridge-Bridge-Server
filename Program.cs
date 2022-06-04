using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace BridgeServer
{
    class Program
    {
        private static int port = 25565;

        public static int bufferSize = 4096;
        public static int sendRate = 60;

        public static int maxConnections = 8;

        public static Connection[] connections = new Connection[maxConnections];

        public static Thread listenThread;

        public static void Main(string[] args)
        {
            listenThread = new Thread(ListenForConnections);
            listenThread.Start();

            while (true)
            {
                foreach (Connection connection in connections)
                {
                    if(connection != null) connection.Update();
                }

                Console.WriteLine("Updating!");
                Thread.Sleep((int)MathF.Floor(1f / 1f * 1000f));
            }
        }

        public static void ListenForConnections()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();

                NetworkStream networkStream = client.GetStream();

                Connection connection = new Connection();

                connection.onPacketRecieved = HandlePacket;

                lock (connections)
                {
                    for (int i = 0; i < connections.Length; i++)
                    {
                        if (connections[i] == null)
                        {
                            connections[i] = connection;
                            break;
                        }
                    }
                }

                connection.Assign(client, networkStream);
            }
        }

        public static void HandlePacket(Connection connection, Packet packet)
        {
            if(packet.packetType == "HOST")
            {
                Console.WriteLine("HOSTING GAME!");
            }else if (packet.packetType == "JOIN")
            {
                Console.WriteLine("JOINING GAME!");
            }
        }
    }
}
