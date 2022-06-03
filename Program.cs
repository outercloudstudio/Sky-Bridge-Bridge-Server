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

        public static Connection connection;

        public static Thread listenThread;

        static void Main(string[] args)
        {
            listenThread = new Thread(ListenForConnections);
            listenThread.Start();

            while (true)
            {
                Console.WriteLine("Enter to continue update loop:");
                string packetContent = Console.ReadLine();

                if(packetContent.Length > 0)
                {
                    connection.SendPacket(new Packet("DEBUG_MESSAGE").AddValue(packetContent));
                }

                connection.Update();
            }
        }

        static void ListenForConnections()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            TcpClient client = listener.AcceptTcpClient();

            NetworkStream networkStream = client.GetStream();

            connection = new Connection();

            connection.Assign(client, networkStream);
        }
    }
}
