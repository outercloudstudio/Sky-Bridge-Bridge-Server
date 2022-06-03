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

        public static List<Room> rooms = new List<Room>();

        public enum ConnectionState
        {
            OFFLINE,
            WAITING_FOR_ACTION,
            HOSTING_ROOM,
            ATTEMPTING_TO_JOIN,
            REJECTED,
            DISCONNECTED,
        }

        public static List<Connection> connections = new List<Connection>();
        public static List<ConnectionState> connectionStates = new List<ConnectionState>();

        static void Main(string[] args)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();

                NetworkStream networkStream = client.GetStream();

                Connection connection = new Connection();
                connection.onPacketRecieved += HandlePacket;
                connection.onConnectionModeUpdated += ConnectionModeUpdated;

                lock (connections)
                {
                    connections.Add(connection);
                    connectionStates.Add(ConnectionState.OFFLINE);
                }

                connection.Assign(client, networkStream);
            }
        }

        public static void HandlePacket(Connection connection, Packet packet)
        {
            if(packet.packetType == Packet.PacketType.HOST_GAME)
            {
                string roomName = (string)packet.values[0].unserializedValue;
                string roomID = (string)packet.values[1].unserializedValue;
                string roomPassword = (string)packet.values[2].unserializedValue;

                Console.WriteLine("Hosting room " + roomName + " with ID " + roomID + " with password " + roomPassword);

                lock (rooms)
                {
                    rooms.Add(new Room()
                    {
                        name = roomName,
                        ID = roomID,
                        password = roomPassword,
                        hostConnection = connection,
                    });
                }

                lock (connections) lock (connectionStates)
                    {
                        connectionStates[connections.FindIndex(con => con.IP == connection.IP && con.port == connection.port)] = ConnectionState.HOSTING_ROOM;
                    }
            }
            else if (packet.packetType == Packet.PacketType.JOIN_GAME)
            {
                string roomID = (string)packet.values[0].unserializedValue;
                string roomPassword = (string)packet.values[1].unserializedValue;

                lock (connections) lock (connectionStates) lock (rooms)
                        {
                            connectionStates[connections.FindIndex(con => con.IP == connection.IP && con.port == connection.port)] = ConnectionState.ATTEMPTING_TO_JOIN;

                            Room room = rooms.Find(room => room.ID == roomID);

                            if (room == null)
                            {
                                connection.QueuePacket(new Packet(Packet.PacketType.ERROR).AddValue(000).AddValue("No Room Found!"));
                                connectionStates[connections.FindIndex(con => con.IP == connection.IP && con.port == connection.port)] = ConnectionState.REJECTED;

                                connection.Disconnect("No Room Found");
                            }
                            else if (room.password != roomPassword)
                            {
                                connection.QueuePacket(new Packet(Packet.PacketType.ERROR).AddValue(001).AddValue("Wrong Password!"));
                                connectionStates[connections.FindIndex(con => con.IP == connection.IP && con.port == connection.port)] = ConnectionState.REJECTED;

                                connection.Disconnect("Wrong Password");
                            }
                            else
                            {
                                room.hostConnection.QueuePacket(new Packet(Packet.PacketType.SIGNAL_JOIN).AddValue(connection.IP).AddValue(connection.port));
                            }
                        }
            }else if (packet.packetType == Packet.PacketType.ERROR)
            {
                int errorCode = (int)packet.values[0].unserializedValue;
                string error = (string)packet.values[1].unserializedValue;

                if(errorCode == 002)
                {
                    string IP = (string)packet.values[2].unserializedValue;
                    int port = (int)packet.values[3].unserializedValue;

                    lock (connections) lock (connectionStates)
                        {

                            Connection failedConnection = connections.Find(con => con.IP == IP && con.port == port);

                            connectionStates[connections.FindIndex(con => con.IP == failedConnection.IP && con.port == failedConnection.port)] = ConnectionState.REJECTED;

                            failedConnection.Disconnect("Max People");
                        }
                }
            }
        }

        public static void ConnectionModeUpdated(Connection connection, Connection.ConnectionMode connectionMode)
        {
            if (connectionMode == Connection.ConnectionMode.CONNECTED)
            {
                connectionStates[connections.FindIndex(con => con.IP == connection.IP && con.port == connection.port)] = ConnectionState.WAITING_FOR_ACTION;

                connection.QueuePacket(new Packet(Packet.PacketType.READY));
            }else if(connectionMode == Connection.ConnectionMode.DISCONNECTED)
            {
                Console.WriteLine("Attempting to remove connection " + connection.IP + ":" + connection.port);

                foreach (Connection con in connections)
                {
                    Console.WriteLine("Found connection " + con.IP + ":" + con.port);
                }

                int index = connections.FindIndex(con => con.IP == connection.IP && con.port == connection.port);

                if (index != -1)
                {
                    connectionStates.RemoveAt(index);
                    connections.Remove(connection);
                }
            }
        }
    }
}
