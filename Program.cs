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

        public static List<Connection> connections = new List<Connection>();

        public static Thread listenThread;

        static void Main(string[] args)
        {
            listenThread = new Thread(ListenForConnections);
            listenThread.Start();

            while (true)
            {
                //Console.WriteLine("Enter to continue update loop:");
                //string packetContent = Console.ReadLine();

                Console.WriteLine("Updating!");

                lock (connections)
                {
                    foreach (Connection connection in connections)
                    {
                        connection.Update();
                    }
                }
            }
        }

        static void ListenForConnections()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();

                NetworkStream networkStream = client.GetStream();

                Connection connection = new Connection();

                lock (connections)
                {
                    connections.Add(connection);
                }

                connection.Assign(client, networkStream);
            }
        }
    }
}
