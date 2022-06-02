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

        public enum ConnectionState
        {
            OFFLINE,
            WAITING_FOR_ACTION,
        }

        public static List<Connection> connections = new List<Connection>();
        public static List<ConnectionState> connectionStates = new List<ConnectionState>();

        static void Main(string[] args)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (true)
            {
                Console.WriteLine("Waiting for connection...");
                TcpClient client = listener.AcceptTcpClient();

                Console.WriteLine("Connection accepted!");
                NetworkStream networkStream = client.GetStream();

                Connection connection = new Connection();
                connection.onPacketRecieved += HandlePacket;
                connection.onConnectionModeUpdated += ConnectionModeUpdated;

                connections.Add(connection);
                connectionStates.Add(ConnectionState.OFFLINE);

                connection.Assign(client, networkStream);
            }
        }

        public static void HandlePacket(Connection connection, Packet packet)
        {

        }

        public static void ConnectionModeUpdated(Connection connection, Connection.ConnectionMode connectionMode)
        {
            Console.WriteLine("Connection " + connection.IP + ":" + connection.port + " updated to mode " + connectionMode);

            if (connectionMode == Connection.ConnectionMode.CONNECTED)
            {
                connectionStates[connections.IndexOf(connection)] = ConnectionState.WAITING_FOR_ACTION;

                Console.WriteLine("Connection updated to state " + connectionStates[connections.IndexOf(connection)]);
            }
        }
    }
}
