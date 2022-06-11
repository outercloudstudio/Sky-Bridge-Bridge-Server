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
        public class Client
        {
            public Connection connection;
            public string ID;

            public Client(Connection _connection)
            {
                connection = _connection;
                ID = Guid.NewGuid().ToString();
            }
        }

        public class Room
        {
            public string ID;
            public Client[] clients;

            public Room(string _ID, int maxConnections)
            {
                ID = _ID;
                clients = new Client[maxConnections];
            }

            public int GetOpenIndex()
            {
                for (int i = 0; i < clients.Length; i++)
                {
                    if (clients[i] == null) return i;
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

                    for (int j = 1; j < room.clients.Length; j++)
                    {
                        if (room.clients[j] == null) continue;

                        Connection connection = room.clients[j].connection;

                        connection.Update(tickDelay);

                        if (connection.connectionMode == Connection.ConnectionMode.DISCONNECTED)
                        {
                            room.clients[j] = null;

                            Console.WriteLine("Removed connection " + connection.IP + ":" + connection.port + " from room " + room.ID );
                        }
                    }

                    if (room.clients[0].connection.connectionMode == Connection.ConnectionMode.DISCONNECTED)
                    {
                        for (int j = 1; j < room.clients.Length; j++)
                        {
                            if (room.clients[j] == null) continue;

                            Connection connection = room.clients[j].connection;

                            connection.Disconnect("Host disconnected!");

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

                Client client = new Client(connection);

                room.clients[0] = client;

                rooms.Add(room);

                connection.SendPacket(new Packet("HOST_INFO").AddValue(ID).AddValue(client.ID));
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

                Client client = new Client(connection);

                room.clients[openIndex] = client;

                lobbyConnections[connectionIndex] = null;

                Console.WriteLine("Offloaded connection " + connection.IP + ":" + connection.port + " from lobbyConnections.");

                connection.SendPacket(new Packet("JOIN_ACCEPTED").AddValue(client.ID));
            }
        }
    }
}
