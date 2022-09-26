using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace SkyBridge
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

        private static int port = 8080;

        public static int bufferSize = 4096;
        public static int bytesPerSecond = bufferSize * 60;
        public static float timeout = 30;
        public static float keepalive = 5;

        public static int maxLobbyConnections = 32;
        public static Connection[] lobbyConnections = new Connection[maxLobbyConnections];

        public static List<Room> rooms = new List<Room>();

        public static void Main(string[] args)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                listener.BeginAcceptTcpClient(new AsyncCallback(AcceptCallback), listener);

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
                                foreach (Client client in room.clients)
                                {
                                    if (client == null) continue;

                                    if (client.connection.connectionMode != Connection.ConnectionMode.CONNECTED) return;

                                    client.connection.SendPacket(new Packet("PLAYER_LEFT").AddValue(room.clients[j].ID));
                                }

                                room.clients[j] = null;

                                Console.WriteLine("Removed connection " + connection.IP + ":" + connection.port + " from room " + room.ID);
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

                    ThreadManager.Update();

                    Thread.Sleep((int)MathF.Floor(tickDelay * 1000f));
                }

                Console.ReadLine();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
                Console.ReadLine();
            }
        }

        public static void AcceptCallback(IAsyncResult result)
        {
            try
            {
                Console.WriteLine("Accepted Client!");

                TcpListener listener = (TcpListener)result.AsyncState;
                TcpClient client = listener.EndAcceptTcpClient(result);

                Connection connection = new Connection();

                connection.onPacketRecieved = (Connection _connection, Packet _packet) => HandlePacket(_connection, _packet);

                for (int i = 0; i < lobbyConnections.Length; i++)
                {
                    if (lobbyConnections[i] == null)
                    {
                        lobbyConnections[i] = connection;
                        break;
                    }
                }

                connection.Assign(client);

                listener.BeginAcceptTcpClient(new AsyncCallback(AcceptCallback), listener);
            }catch(Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public static void HandlePacket(Connection connection, Packet packet, Room room = null)
        {
            if (packet.packetType == "HOST")
            {
                string ID = Guid.NewGuid().ToString();
                int maxConnections = packet.GetInt(0);

                Console.WriteLine("Hosted room " + ID);

                Room newRoom = new Room(ID, maxConnections);

                Client client = new Client(connection);

                newRoom.clients[0] = client;

                rooms.Add(newRoom);

                connection.onPacketRecieved = (Connection _connection, Packet _packet) => HandlePacket(_connection, _packet, newRoom);

                connection.SendPacket(new Packet("HOST_INFO").AddValue(ID).AddValue(client.ID));
            }else if (packet.packetType == "JOIN")
            {
                int connectionIndex = Array.IndexOf(lobbyConnections, connection);

                string roomID = packet.GetString(0);

                Console.WriteLine("Joining room " + roomID);

                Room currentRoom = rooms.Find(_room => _room.ID == roomID);

                if(currentRoom == null)
                {
                    connection.SendPacket(new Packet("JOIN_REJECTED").AddValue("Room does not exist!"));

                    return;
                }

                int openIndex = currentRoom.GetOpenIndex();

                if (openIndex == -1)
                {
                    connection.SendPacket(new Packet("JOIN_REJECTED").AddValue("Room is full!"));

                    return;
                }

                Client client = new Client(connection);

                foreach (Client _client in currentRoom.clients)
                {
                    if (_client == null) continue;

                    _client.connection.SendPacket(new Packet("PLAYER_JOINED").AddValue(client.ID));
                }

                currentRoom.clients[openIndex] = client;

                lobbyConnections[connectionIndex] = null;

                connection.onPacketRecieved = (Connection _connection, Packet _packet) => HandlePacket(_connection, _packet, currentRoom);

                Console.WriteLine("Offloaded connection " + connection.IP + ":" + connection.port + " from lobbyConnections.");

                connection.SendPacket(new Packet("JOIN_ACCEPTED").AddValue(client.ID).AddValue(currentRoom.clients.Length));
            }else if (packet.packetType == "RELAY")
            {
                Console.WriteLine(packet.GetString(0));

                string target = packet.GetString(1);

                packet.values[1] = new Packet.SerializedValue(Array.Find(room.clients, client => client != null && client.connection == connection).ID);

                int targetIndex = Array.FindIndex(room.clients, client => client != null && client.ID == target);

                if (target == "host") targetIndex = 0;

                if (targetIndex == -1) return;

                room.clients[targetIndex].connection.SendPacket(packet, packet.reliability);
            }
        }
    }
}
