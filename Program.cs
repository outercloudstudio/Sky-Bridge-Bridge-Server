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
        public class Room
        {
            public string ID;
        }

        private static int port = 25565;

        public static int bufferSize = 4096;
        public static int sendRate = 60;
        public static float timeout = 30;
        public static float keepalive = 5;

        public static int maxLobbyConnections = 4;
        public static Connection[] lobbyConnections = new Connection[maxLobbyConnections];

        public static List<Room> rooms = new List<Room>();

        public static Thread listenThread;

        public static void Main(string[] args)
        {
            listenThread = new Thread(ListenForConnections);
            listenThread.Start();

            while (true)
            {
                for (int i = 0; i < lobbyConnections.Length; i++)
                {
                    Connection connection = lobbyConnections[i];

                    if(connection != null)
                    {
                        connection.Update(1);

                        if(connection.connectionMode == Connection.ConnectionMode.DISCONNECTED)
                        {
                            lobbyConnections[i] = null;
                        }
                    }
                }

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

                lock (lobbyConnections)
                {
                    for (int i = 0; i < lobbyConnections.Length; i++)
                    {
                        if (lobbyConnections[i] == null)
                        {
                            lobbyConnections[i] = connection;
                            break;
                        }
                    }
                }

                connection.Assign(client, networkStream);
            }
        }

        public static void HandlePacket(Connection connection, Packet packet)
        {
            if (packet.packetType == "HOST")
            {
                string ID = Guid.NewGuid().ToString();

                Console.WriteLine("Hosted room " + ID);

                rooms.Add(new Room()
                {
                    ID = ID
                });

                connection.SendPacket(new Packet("HOST_INFO").AddValue(ID));
            }else if (packet.packetType == "JOIN")
            {
                int connectionIndex = Array.IndexOf(lobbyConnections, connection);

                string roomID = packet.GetString(0);

                Console.WriteLine("Joining room " + roomID);

                Room room = rooms.Find(_room => _room.ID == roomID);

                if(room == null)
                {
                    connection.SendPacket(new Packet("JOIN_ERROR").AddValue("Room does not exist!"));

                    return;
                }

                //Check if room is full
                //connection.SendPacket(new Packet("JOIN_REJECTED").AddValue("Room is full!"));

                //Add connection to room + remove from connections

                connection.SendPacket(new Packet("JOIN_ACCEPTED"));
            }
        }
    }
}
