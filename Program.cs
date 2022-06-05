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
            public Connection host;
        }

        public class JoinAttempt
        {
            public string ID;
            public string roomID;
        }

        private static int port = 25565;

        public static int bufferSize = 4096;
        public static int sendRate = 60;
        public static float timeout = 30;
        public static float keepalive = 5;

        public static int maxConnections = 4;

        public static Connection[] connections = new Connection[maxConnections];
        public static JoinAttempt[] joinAttempts = new JoinAttempt[maxConnections];

        public static List<Room> rooms = new List<Room>();

        public static Thread listenThread;

        public static void Main(string[] args)
        {
            listenThread = new Thread(ListenForConnections);
            listenThread.Start();

            while (true)
            {
                for (int i = 0; i < rooms.Count; i++)
                {
                    Room room = rooms[i];

                    if (room.host.connectionMode == Connection.ConnectionMode.DISCONNECTED)
                    {
                        Console.WriteLine("Removing room " + room.ID + " because host disconnected!");

                        rooms.RemoveAt(i);

                        i--;
                    }
                }

                for (int i = 0; i < connections.Length; i++)
                {
                    Connection connection = connections[i];

                    if(connection != null)
                    {
                        connection.Update(1);

                        if(connection.connectionMode == Connection.ConnectionMode.DISCONNECTED)
                        {
                            connections[i] = null;
                            joinAttempts[i] = null;
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
            if (packet.packetType == "HOST")
            {
                string ID = Guid.NewGuid().ToString();

                Console.WriteLine("Hosted room " + ID);

                rooms.Add(new Room()
                {
                    ID = ID,
                    host = connection
                });

                connection.SendPacket(new Packet("HOST_INFO").AddValue(ID));
            }else if (packet.packetType == "JOIN")
            {
                int connectionIndex = Array.IndexOf(connections, connection);

                if (joinAttempts[connectionIndex] != null)
                {
                    connection.SendPacket(new Packet("JOIN_ERROR").AddValue("Already attempting to join room!"));

                    return;
                }

                string roomID = packet.GetString(0);

                Console.WriteLine("Joining room " + roomID);

                Room room = rooms.Find(_room => _room.ID == roomID);

                if(room == null)
                {
                    connection.SendPacket(new Packet("JOIN_ERROR").AddValue("Room does not exist!"));

                    return;
                }

                string ID = Guid.NewGuid().ToString();

                joinAttempts[connectionIndex] = new JoinAttempt()
                {
                    ID = ID,
                    roomID = roomID
                };

                Console.WriteLine("Created join attempt for room " + roomID + " with ID " + ID);

                room.host.SendPacket(new Packet("JOIN_ATTEMPT").AddValue(ID).AddValue(connection.IP));
            }else if (packet.packetType == "JOIN_ATTEMPT_REJECTED")
            {
                string ID = packet.GetString(0);
                string reason = packet.GetString(1);

                int joinAttemptIndex = Array.FindIndex(joinAttempts, _joinAttempt => _joinAttempt != null && _joinAttempt.ID == ID);

                if (joinAttemptIndex == -1) return;

                connections[joinAttemptIndex].SendPacket(new Packet("JOIN_ATTEMPT_REJECTED").AddValue(reason));
            }
            else if (packet.packetType == "JOIN_ATTEMPT_ACCEPTED")
            {
                string ID = packet.GetString(0);
                int port = packet.GetInt(1);

                int joinAttemptIndex = Array.FindIndex(joinAttempts, _joinAttempt => _joinAttempt != null && _joinAttempt.ID == ID);

                if (joinAttemptIndex == -1) return;

                Room room = rooms.Find(room => room.ID == joinAttempts[joinAttemptIndex].roomID);

                if(room == null) return;

                connections[joinAttemptIndex].SendPacket(new Packet("JOIN_ATTEMPT_ACCEPTED").AddValue(room.host.IP).AddValue(port));
            }
        }
    }
}
