using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace SkyBridge
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

        private TcpClient? TCPClient;
        private UdpClient? UDPClient;
        private int UDPPort;
        private NetworkStream? networkStream;
        private byte[] networkStreamBuffer; 
        private IPEndPoint remoteIpEndPoint;

        public delegate void PacketRecieved(Connection connection, Packet packet);
        public PacketRecieved onPacketRecieved;

        public string IP;
        public int port;

        private float timeout = SkyBridge.timeout;
        private float keepalive = SkyBridge.keepalive;

        public List<byte> sendQueue = new List<byte>();
        public int byteCounter;
        public List<Packet> unreliableSendQueue = new List<Packet>();
        public List<byte> readStream = new List<byte>();
        private bool waitingForUDP = false;

        // Sets connection up for connection and begins connection asyncs
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

        // Result of TCP Connection
        public void ConnectCallback(IAsyncResult result)
        {
            try
            {
                if (!TCPClient.Connected) Disconnect("Failed to connect!");

                TCPClient.EndConnect(result);

                networkStream = TCPClient.GetStream();

                networkStreamBuffer = new byte[SkyBridge.bufferSize];
                networkStream.BeginRead(networkStreamBuffer, 0, SkyBridge.bufferSize, new AsyncCallback(ReceiveCallback), null);

                ThreadManager.ExecuteOnMainThread(() =>
                {
                    SendPacket(new Packet("UDP_INFO").AddValue(UDPPort));
                });

                connectionMode = ConnectionMode.CONNECTED;
            }catch(Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        // Creates connection from an existing tcp client
        public void Assign(TcpClient _TCPClient)
        {
            IP = ((IPEndPoint)_TCPClient.Client.RemoteEndPoint).Address.ToString();
            port = ((IPEndPoint)_TCPClient.Client.RemoteEndPoint).Port;

            UDPPort = GetOpenPort();

            UDPClient = new UdpClient(UDPPort);
            remoteIpEndPoint = new IPEndPoint(IPAddress.Any, UDPPort);

            TCPClient = _TCPClient;
            networkStream = _TCPClient.GetStream();

            networkStreamBuffer = new byte[SkyBridge.bufferSize];
            networkStream.BeginRead(networkStreamBuffer, 0, SkyBridge.bufferSize, new AsyncCallback(ReceiveCallback), null);

            ThreadManager.ExecuteOnMainThread(() =>
            {
                SendPacket(new Packet("UDP_INFO").AddValue(UDPPort));
            });

            connectionMode = ConnectionMode.CONNECTED;
        }

        // Begins UDP Listening
        public void BeginUDP(int port)
        {
            UDPClient.Connect(IP, port);

            UDPClient.BeginReceive(new AsyncCallback(ReceiveUnreliableCallback), null);
        }

        // Queues up a packet
        public void SendPacket(Packet packet, PacketReliability reliability = PacketReliability.RELIABLE)
        {
            if (reliability == PacketReliability.RELIABLE)
            {
                // if (packet.packetType != "KEEP_ALIVE") Console.WriteLine("Sending packet " + packet.packetType + " to " + IP + ":" + port);
                sendQueue.AddRange(packet.ToBytes());
            }
            else
            {
                unreliableSendQueue.Add(packet);
            }
        }

        // Unused, Called when TCP packet is sent
        public void SendCallback(IAsyncResult result)
        {

        }

        // Sends TCP messages, call itselft to send as fast as possible
        public void UnreliableSendCallback(IAsyncResult result)
        {
            try
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

                // Console.WriteLine("Sending unreliable packet " + packet.packetType + " to " + IP + ":" + port);

                try
                {
                    UDPClient.BeginSend(packetBytes, packetBytes.Length, UnreliableSendCallback, null);
                }
                catch (Exception ex)
                {
                    Disconnect("Unreliable Send Error");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        // Callback when received a TCP piece
        public void ReceiveCallback(IAsyncResult result)
        {
            try
            {
                try
                {
                    if (networkStream == null) return;

                    int bytesRead = networkStream.EndRead(result);
                    readStream.AddRange(networkStreamBuffer[0..bytesRead]);
                }
                catch (Exception ex)
                {
                    Disconnect("Listen Error");
                }

                // Tell connection to read TCP again
                ThreadManager.ExecuteOnMainThread(() =>
                {
                    try
                    {
                        networkStream.BeginRead(networkStreamBuffer, 0, SkyBridge.bufferSize, new AsyncCallback(ReceiveCallback), null);
                    }
                    catch (Exception ex)
                    {
                        Disconnect("Listen Error");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        // Callback when a UDP message is receieved
        public void ReceiveUnreliableCallback(IAsyncResult result)
        {
            try
            {
                if(connectionMode != ConnectionMode.CONNECTED) return;

                byte[] bytes = UDPClient.EndReceive(result, ref remoteIpEndPoint);

                Packet packet = new Packet(bytes, PacketReliability.UNRELIABLE);

                // Console.WriteLine("Recieved unreliable packet " + packet.packetType + " from " + IP + ":" + port);

                ThreadManager.ExecuteOnMainThread(() =>
                {
                    if (onPacketRecieved != null) onPacketRecieved(this, packet);

                    try
                    {
                        UDPClient.BeginReceive(new AsyncCallback(ReceiveUnreliableCallback), null);
                    }
                    catch (Exception ex)
                    {
                        Disconnect("Unreliable Listen Error");
                    }
                });
            }
            catch (Exception ex)
            {
                Disconnect("Unreliable Listen Error");
            }
        }

        // Run every tick, handles timing, sends TCP messages, and handles readStream
        public void Update(float delta)
        {
            timeout -= delta;
            keepalive -= delta;

            if (keepalive <= 0)
            {
                SendPacket(new Packet("KEEP_ALIVE"));
            
                keepalive = SkyBridge.keepalive;
            }

            byteCounter = Math.Max(byteCounter - (int)Math.Floor(SkyBridge.bytesPerSecond * (decimal)delta), 0);

            if (byteCounter == 0)
            {
                try
                {
                    networkStream.BeginWrite(sendQueue.ToArray(), 0, Math.Min(SkyBridge.bufferSize, sendQueue.Count), SendCallback, null);

                    byteCounter += Math.Min(SkyBridge.bufferSize, sendQueue.Count);

                    sendQueue.RemoveRange(0, Math.Min(SkyBridge.bufferSize, sendQueue.Count));
                }
                catch (Exception ex)
                {
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
                        timeout = SkyBridge.timeout;
                    }
                    else if (packet.packetType == "UDP_INFO")
                    {
                        int UDPPort = packet.GetInt(0);

                        BeginUDP(UDPPort);
                    }
                    else
                    {
                        // Console.WriteLine("Recieved packet " + packet.packetType + " from " + IP + ":" + port);

                        if (onPacketRecieved != null) onPacketRecieved(this, packet);
                    }

                    readStream.RemoveRange(0, packetLength);
                }
            }

            if (unreliableSendQueue.Count > 0 && !waitingForUDP) UnreliableSendCallback(null);

            if (timeout <= 0) Disconnect("Connection timed out");
        }

        // Disconnects Connection
        public void Disconnect(string reason = "unkown")
        {
            if (connectionMode == ConnectionMode.DISCONNECTED) return;

            Console.WriteLine("Disconnected connection " + IP + ":" + port + " because " + reason);

            connectionMode = ConnectionMode.DISCONNECTED;

            if (TCPClient != null && networkStream != null && TCPClient.Connected && UDPClient != null)
            {
                TcpClient clientToClose = TCPClient;
                UdpClient udpClientToClose = UDPClient;
                NetworkStream networkStreamToClose = networkStream;

                TCPClient = null;
                UDPClient = null;
                networkStream = null;
                
                clientToClose.Close();
                udpClientToClose.Close();
                networkStreamToClose.Close();
            }
        }

        // Gets an open port
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
