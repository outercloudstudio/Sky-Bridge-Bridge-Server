using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BridgeServer
{
    [Serializable]
    public class Connection
    {
        public enum ConnectionMode
        {
            OFFLINE,
            CONNECTING,
            CONNECTED,
            DISCONNECTED
        }

        public enum PacketReliability
        {
            RELIABLE,
            UNRELIABLE
        }

        public ConnectionMode connectionMode = ConnectionMode.OFFLINE;

        private TcpClient TCPClient;
        private UdpClient UDPClient;
        private int UDPPort;
        private NetworkStream networkStream;
        private byte[] networkStreamBuffer; 
        private IPEndPoint remoteIpEndPoint;

        public delegate void PacketRecieved(Connection connection, Packet packet);
        public PacketRecieved onPacketRecieved;

        public string IP;
        public int port;

        private float timeout = Program.timeout;
        private float keepalive = Program.keepalive;

        public List<Packet> sendQueue = new List<Packet>();

        public void Connect(string _IP, int _port)
        {
            IP = _IP;
            port = _port;

            connectionMode = ConnectionMode.CONNECTING;

            UDPPort = GetOpenPort();

            UDPClient = new UdpClient(UDPPort);
            remoteIpEndPoint = new IPEndPoint(IPAddress.Any, UDPPort);

            TCPClient = new TcpClient();
            TCPClient.BeginConnect(_IP, _port, new AsyncCallback(ConnectCallback), null);
        }

        public void ConnectCallback(IAsyncResult result)
        {
            TCPClient.EndConnect(result);

            if (!TCPClient.Connected)
            {
                Disconnect("Failed to connect!");

                return;
            }

            networkStream = TCPClient.GetStream();

            networkStreamBuffer = new byte[Program.bufferSize];
            networkStream.BeginRead(networkStreamBuffer, 0, Program.bufferSize, new AsyncCallback(ReceiveCallback), null);

            ThreadManager.ExecuteOnMainThread(() =>
            {
                SendPacket(new Packet("UDP_INFO").AddValue(UDPPort));
            });

            connectionMode = ConnectionMode.CONNECTED;
        }

        public void Assign(TcpClient _TCPClient)
        {
            IP = ((IPEndPoint)_TCPClient.Client.RemoteEndPoint).Address.ToString();
            port = ((IPEndPoint)_TCPClient.Client.RemoteEndPoint).Port;

            UDPPort = GetOpenPort();

            UDPClient = new UdpClient(UDPPort);
            remoteIpEndPoint = new IPEndPoint(IPAddress.Any, UDPPort);

            TCPClient = _TCPClient;
            networkStream = _TCPClient.GetStream();

            networkStreamBuffer = new byte[Program.bufferSize];
            networkStream.BeginRead(networkStreamBuffer, 0, Program.bufferSize, new AsyncCallback(ReceiveCallback), null);

            ThreadManager.ExecuteOnMainThread(() =>
            {
                SendPacket(new Packet("UDP_INFO").AddValue(UDPPort));
            });

            connectionMode = ConnectionMode.CONNECTED;
        }

        public void BeginUDP(int port)
        {
            UDPClient.Connect(IP, port);

            UDPClient.BeginReceive(new AsyncCallback(ReceiveUnreliableCallback), null);
        }

        public void SendPacket(Packet packet, PacketReliability reliability = PacketReliability.RELIABLE)
        {
            packet.reliability = reliability;

            sendQueue.Add(packet);

            if (sendQueue.Count == 1) SendCallback(null);
        }

        public void SendCallback(IAsyncResult result)
        {
            if (sendQueue.Count == 0) return;

            Packet packet = sendQueue[0];

            sendQueue.RemoveAt(0);

            byte[] packetBytes = packet.ToBytes();

            if (packet.reliability == PacketReliability.RELIABLE)
            {
                if(packet.packetType != "KEEP_ALIVE") Console.WriteLine("Sending packet " + packet.packetType + " to " + IP + ":" + port);

                try
                {
                    networkStream.BeginWrite(packetBytes, 0, packetBytes.Length, SendCallback, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);

                    Disconnect("Send Error");
                }
            }
            else
            {
                Console.WriteLine("Sending unreliable packet " + packet.packetType + " to " + IP + ":" + port);

                try
                {
                    UDPClient.BeginSend(packetBytes, packetBytes.Length, SendCallback, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);

                    Disconnect("Unreliable Send Error");
                }
            }
        }

        public void ReceiveCallback(IAsyncResult result)
        {
            try
            {
                int bytesRead = networkStream.EndRead(result);

                for (int readPos = 0; readPos < bytesRead;)
                {
                    byte[] packetLengthBytes = networkStreamBuffer[readPos..(readPos + 4)];
                    int packetLength = BitConverter.ToInt32(packetLengthBytes);

                    byte[] packetBytes = networkStreamBuffer[readPos..(readPos + packetLength)];

                    Packet packet = new Packet(packetBytes, PacketReliability.RELIABLE);

                    if (packet.packetType == "KEEP_ALIVE")
                    {
                        timeout = Program.timeout;
                    }
                    else if (packet.packetType == "UDP_INFO")
                    {
                        int UDPPort = packet.GetInt(0);

                        BeginUDP(UDPPort);
                    }
                    else
                    {
                        Console.WriteLine("Recieved packet " + packet.packetType + " from " + IP + ":" + port);

                        ThreadManager.ExecuteOnMainThread(() =>
                        {
                            if (onPacketRecieved != null) onPacketRecieved(this, packet);
                        });
                    }

                    readPos += packetLength;
                }

                ThreadManager.ExecuteOnMainThread(() =>
                {
                    networkStream.BeginRead(networkStreamBuffer, 0, Program.bufferSize, new AsyncCallback(ReceiveCallback), null);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                Disconnect("Listen Error");
            }
        }

        public void ReceiveUnreliableCallback(IAsyncResult result)
        {
            try
            {
                byte[] bytes = UDPClient.EndReceive(result, ref remoteIpEndPoint);

                Packet packet = new Packet(bytes, PacketReliability.UNRELIABLE);

                Console.WriteLine("Recieved unreliable packet " + packet.packetType + " from " + IP + ":" + port);

                ThreadManager.ExecuteOnMainThread(() =>
                {
                    if (onPacketRecieved != null) onPacketRecieved(this, packet);
                });

                ThreadManager.ExecuteOnMainThread(() =>
                {
                    UDPClient.BeginReceive(new AsyncCallback(ReceiveUnreliableCallback), null);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                Disconnect("Unreliable Listen Error");
            }
        }

        public void Update(float delta)
        {
            if (connectionMode != ConnectionMode.CONNECTED) return;

            timeout -= delta;
            keepalive -= delta;

            for (int i = 0; i < Math.Min(sendQueue.Count, 1); i++)
            {
                Packet packet = sendQueue[i];

                byte[] packetBytes = packet.ToBytes();

                if (packet.reliability == PacketReliability.RELIABLE)
                {
                    Console.WriteLine("Sending packet " + packet.packetType + " to " + IP + ":" + port);

                    try
                    {
                        networkStream.BeginWrite(packetBytes, 0, packetBytes.Length, null, null);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);

                        Disconnect("Send Error");
                    }
                }
                else
                {
                    Console.WriteLine("Sending unreliable packet " + packet.packetType + " to " + IP + ":" + port);

                    try
                    {
                        UDPClient.BeginSend(packetBytes, packetBytes.Length, null, null);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);

                        Disconnect("Unreliable Send Error");
                    }
                }
            }

            if (keepalive <= 0)
            {
                SendPacket(new Packet("KEEP_ALIVE"));
            
                keepalive = Program.keepalive;
            }

            if (timeout <= 0) Disconnect("Connection timed out");
        }

        public void Disconnect(string reason = "unkown")
        {
            if (connectionMode == ConnectionMode.DISCONNECTED) return;

            Console.WriteLine("Disconnected connection " + IP + ":" + port + " because " + reason);

            connectionMode = ConnectionMode.DISCONNECTED;

            if (TCPClient != null && networkStream != null && TCPClient.Connected && UDPClient != null)
            {
                TCPClient.Close();
                UDPClient.Close();
                networkStream.Close();
            }
        }

        public static int GetOpenPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);

            l.Start();

            int port = ((IPEndPoint)l.LocalEndpoint).Port;

            l.Stop();

            return port;
        }
    }
}
