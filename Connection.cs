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

        public List<byte> sendQueue = new List<byte>();
        public int byteCounter;
        public List<Packet> unreliableSendQueue = new List<Packet>();
        public List<byte> readStream = new List<byte>();
        private bool waitingForUDP = false;

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
            if (reliability == PacketReliability.RELIABLE)
            {
                if (packet.packetType != "KEEP_ALIVE") Console.WriteLine("Sending packet " + packet.packetType + " to " + IP + ":" + port);
                sendQueue.AddRange(packet.ToBytes());
            }
            else
            {
                unreliableSendQueue.Add(packet);
            }
        }

        public void SendCallback(IAsyncResult result)
        {

        }

        public void UnreliableSendCallback(IAsyncResult result)
        {
            if (unreliableSendQueue.Count == 0)
            {
                waitingForUDP = false;

                return;
            }

            waitingForUDP = true;
            Packet packet = unreliableSendQueue[0];

            byte[] packetBytes = packet.ToBytes();

            unreliableSendQueue.RemoveAt(0);

            Console.WriteLine("Sending unreliable packet " + packet.packetType + " to " + IP + ":" + port);

            try
            {
                UDPClient.BeginSend(packetBytes, packetBytes.Length, UnreliableSendCallback, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Disconnect("Unreliable Send Error");
            }
        }

        public void ReceiveCallback(IAsyncResult result)
        {
            try
            {
                int bytesRead = networkStream.EndRead(result);
                readStream.AddRange(networkStreamBuffer[0..bytesRead]);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                Disconnect("Listen Error");
            }

            ThreadManager.ExecuteOnMainThread(() =>
            {
                networkStream.BeginRead(networkStreamBuffer, 0, Program.bufferSize, new AsyncCallback(ReceiveCallback), null);
            });
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

            if (keepalive <= 0)
            {
                SendPacket(new Packet("KEEP_ALIVE"));
            
                keepalive = Program.keepalive;
            }

            byteCounter = Math.Max(byteCounter - (int)Math.Floor(Program.bytesPerSecond * (decimal)delta), 0);

            if (byteCounter == 0)
            {
                try
                {
                    networkStream.BeginWrite(sendQueue.ToArray(), 0, Math.Min(Program.bufferSize, sendQueue.Count), SendCallback, null);

                    byteCounter += Math.Min(Program.bufferSize, sendQueue.Count);

                    sendQueue.RemoveRange(0, Math.Min(Program.bufferSize, sendQueue.Count));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);

                    Disconnect("Send Error");
                }
            }

            if (readStream.Count > 0)
            {
                while (true)
                {
                    if (readStream.Count < 4) break;

                    int packetLength = BitConverter.ToInt32(readStream.GetRange(0, 4).ToArray());

                    if (readStream.Count < packetLength) break;

                    Packet packet = new Packet(readStream.GetRange(0, packetLength).ToArray());

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

                        if (onPacketRecieved != null) onPacketRecieved(this, packet);
                    }

                    readStream.RemoveRange(0, packetLength);
                }
            }

            if (unreliableSendQueue.Count > 0 && !waitingForUDP) UnreliableSendCallback(null);

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
