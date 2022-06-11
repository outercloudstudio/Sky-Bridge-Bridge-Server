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
            public Connection[] connections;

            public Room(string _ID, int maxConnections)
            {
                ID = _ID;
                connections = new Connection[maxConnections];
            }

            public int GetOpenIndex()
            {
                for (int i = 0; i < connections.Length; i++)
                {
                    if (connections[i] == null) return i;
                }

                return -1;
            }
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

            float tickDelay = 1f / 60f;

            while (true)
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    Room room = rooms[i];

                    if (room == null) continue;

                    for (int j = 1; j < room.connections.Length; j++)
                    {
                        Connection connection = room.connections[i];

                        if (connection == null) continue;

                        connection.Update(tickDelay);

                        if (connection.connectionMode == Connection.ConnectionMode.DISCONNECTED)
                        {
                            room.connections[i] = null;

                            Console.WriteLine("Removed connection " + connection.IP + ":" + connection.port + " from room " + room.ID );
                        }
                    }

                    if (room.connections[0].connectionMode == Connection.ConnectionMode.DISCONNECTED)
                    {
                        for (int j = i; j < room.connections.Length; j++)
                        {
                            Connection connection = room.connections[i];

                            if (connection == null || connection.connectionMode != Connection.ConnectionMode.CONNECTED) continue;

                            connection.Disconnect();

                            Console.WriteLine("Disconnected connection " + connection.IP + ":" + connection.port + " from room " + room.ID + " because host disconnected");
                        }

                        rooms.RemoveAt(i);
                        i--;

                        Console.WriteLine("Removed room " + room.ID);
                    }
                }

                for (int i = 0; i < lobbyConnections.Length; i++)
                {
                    Connection connection = lobbyConnections[i];

                    if (connection == null) continue;

                    connection.Update(tickDelay);

                    if (connection.connectionMode == Connection.ConnectionMode.DISCONNECTED)
                    {
                        lobbyConnections[i] = null;

                        Console.WriteLine("Removed connection " + connection.IP + ":" + connection.port + " from lobbyConnections");
                    }
                }

                Thread.Sleep((int)MathF.Floor(tickDelay * 1000f));
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
                int maxConnections = packet.GetInt(0);

                Console.WriteLine("Hosted room " + ID);

                Room room = new Room(ID, maxConnections);

                room.connections[0] = connection;

                rooms.Add(room);

                connection.SendPacket(new Packet("HOST_INFO").AddValue(ID));
            }else if (packet.packetType == "JOIN")
            {
                int connectionIndex = Array.IndexOf(lobbyConnections, connection);

                string roomID = packet.GetString(0);

                Console.WriteLine("Joining room " + roomID);

                Room room = rooms.Find(_room => _room.ID == roomID);

                if(room == null)
                {
                    connection.SendPacket(new Packet("JOIN_REJECTED").AddValue("Room does not exist!"));

                    return;
                }

                int openIndex = room.GetOpenIndex();

                if (openIndex == -1)
                {
                    connection.SendPacket(new Packet("JOIN_REJECTED").AddValue("Room is full!"));

                    return;
                }

                room.connections[openIndex] = connection;

                lobbyConnections[connectionIndex] = null;

                Console.WriteLine("Offloaded connection " + connection.IP + ":" + connection.port + " from lobbyConnections.");

                connection.SendPacket(new Packet("JOIN_ACCEPTED"));
            }
        }
    }
}
