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

        private Thread connectThread;

        private Thread dataListenerThread;
        private Thread dataListenerUnreliableThread;
        private Thread dataSenderThread;

        private TcpClient TCPClient;
        private UdpClient UDPClient;
        private NetworkStream networkStream;
        private IPEndPoint remoteIpEndPoint;

        public delegate void PacketRecieved(Connection connection, Packet packet);
        public PacketRecieved onPacketRecieved;

        public string IP;
        public int port;

        private float timeout = Program.timeout;
        private float keepalive = Program.keepalive;

        private List<Packet> sendQueue = new List<Packet>();
        private List<Packet> sendQueueUnreliable = new List<Packet>();

        private List<Packet> readQueue = new List<Packet>();

        public void Connect(string _IP, int _port)
        {
            IP = _IP;
            port = _port;

            connectionMode = ConnectionMode.CONNECTING;

            connectThread = new Thread(ConnectThreaded);
            connectThread.Start();
        }

        public void Assign(TcpClient _TCPClient, NetworkStream _networkStream)
        {
            IP = ((IPEndPoint)_TCPClient.Client.RemoteEndPoint).Address.ToString();
            port = ((IPEndPoint)_TCPClient.Client.RemoteEndPoint).Port;

            int localPort = ((IPEndPoint)_TCPClient.Client.LocalEndPoint).Port;

            TCPClient = _TCPClient;
            UDPClient = new UdpClient(localPort);

            UDPClient.Connect(IP, port);

            networkStream = _networkStream;
            remoteIpEndPoint = new IPEndPoint(IPAddress.Any, localPort);

            connectionMode = ConnectionMode.CONNECTED;

            StartThreads();
        }

        public void ConnectThreaded()
        {
            try
            {
                TCPClient = new TcpClient(IP, port);

                networkStream = TCPClient.GetStream();

                int localPort = ((IPEndPoint)TCPClient.Client.LocalEndPoint).Port;

                UDPClient = new UdpClient(localPort);
                remoteIpEndPoint = new IPEndPoint(IPAddress.Any, localPort);

                UDPClient.Connect(IP, port);

                connectionMode = ConnectionMode.CONNECTED;

                StartThreads();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                Disconnect("Failed to connect!");
            }
        }

        public void StartThreads()
        {
            dataSenderThread = new Thread(SendLoop);
            dataListenerThread = new Thread(ListenLoop);
            dataListenerUnreliableThread = new Thread(ListenLoopUnreliable);

            dataSenderThread.Start();
            dataListenerThread.Start();
            dataListenerUnreliableThread.Start();
        }

        public void SendPacket(Packet packet, PacketReliability reliability = PacketReliability.RELIABLE)
        {
            lock (sendQueue) lock (sendQueueUnreliable)
                {
                    Console.WriteLine("Main Thread: Sending packet " + packet.packetType + " to " + IP + ":" + port + " " + reliability);
                    if (reliability == PacketReliability.RELIABLE)
                    {
                        sendQueue.Add(packet);
                    }
                    else
                    {
                        sendQueueUnreliable.Add(packet);
                    }
                }
        }

        public void Update(float delta)
        {
            timeout -= delta;
            keepalive -= delta;

            lock (readQueue)
            {
                foreach (Packet packet in readQueue)
                {
                    Console.WriteLine("Main Thread: Handleing packet " + packet.packetType + " from " + IP + ":" + port);
                    if (onPacketRecieved != null) onPacketRecieved(this, packet);
                }

                readQueue = new List<Packet>();
            }

            if (timeout <= 0) Disconnect("Connection timed out");
        }

        public void SendLoop()
        {
            try
            {
                while (true)
                {
                    lock (sendQueue) lock (readQueue)
                        {
                            if (sendQueue.Count > 0 || keepalive <= 0)
                            {
                                byte[] sendBuffer = new byte[0];

                                int packetsPacked = 0;

                                if (keepalive <= 0)
                                {
                                    keepalive = Program.keepalive;

                                    Packet packet = new Packet("KEEP_ALIVE");

                                    byte[] packetBytes = packet.ToBytes();

                                    if (sendBuffer.Length + packetBytes.Length < Program.bufferSize)
                                    {
                                        //Console.WriteLine("Send Thread: Sending Packet " + packet.packetType.ToString() + " to " + IP + ":" + port);

                                        sendBuffer = packetBytes;

                                        packetsPacked++;
                                    }
                                }

                                while (true)
                                {
                                    if (sendQueue.Count == 0) break;

                                    Packet packet = sendQueue[0];

                                    byte[] packetBytes = packet.ToBytes();

                                    if (sendBuffer.Length + packetBytes.Length >= Program.bufferSize) break;

                                    Console.WriteLine("Send Thread: Sending Packet " + packet.packetType.ToString() + " to " + IP + ":" + port);

                                    byte[] extendedBytes = new byte[sendBuffer.Length + packetBytes.Length];

                                    Buffer.BlockCopy(sendBuffer, 0, extendedBytes, 0, sendBuffer.Length);

                                    Buffer.BlockCopy(packetBytes, 0, extendedBytes, sendBuffer.Length, packetBytes.Length);

                                    sendBuffer = extendedBytes;

                                    packetsPacked++;

                                    sendQueue.RemoveAt(0);
                                }

                                if (packetsPacked == 0)
                                {
                                    Packet packet = sendQueue[0];

                                    Console.WriteLine("Send Thread: Dropping Packet Because It Is Too Large To Send! " + packet.packetType + " Length: " + packet.ToBytes().Length);

                                    sendQueue.RemoveAt(0);
                                }

                                networkStream.Write(sendBuffer, 0, sendBuffer.Length);
                            }

                            foreach (Packet packet in sendQueueUnreliable)
                            {
                                byte[] packetBytes = packet.ToBytes();

                                UDPClient.Send(packetBytes, packetBytes.Length);
                            }

                            sendQueueUnreliable = new List<Packet>();
                        }

                    Thread.Sleep((int)MathF.Floor(1f / Program.sendRate * 1000f));
                }
            }
            catch
            {
                Disconnect("Send Error");
            }
        }

        public void ListenLoop()
        {
            try
            {
                while (true)
                {
                    byte[] bytes = new byte[4096];

                    int bytesRead = networkStream.Read(bytes, 0, bytes.Length);

                    lock (readQueue)
                    {
                        for (int readPos = 0; readPos < bytesRead;)
                        {
                            byte[] packetLengthBytes = bytes[readPos..(readPos + 4)];
                            int packetLength = BitConverter.ToInt32(packetLengthBytes);

                            byte[] packetBytes = bytes[readPos..(readPos + packetLength)];

                            Packet packet = new Packet(packetBytes);

                            if (packet.packetType == "KEEP_ALIVE")
                            {
                                timeout = Program.timeout;
                            }
                            else
                            {
                                Console.WriteLine("Listend Thread: Recieved packet " + packet.packetType + " from " + IP + ":" + port);

                                readQueue.Add(packet);
                            }

                            readPos += packetLength;
                        }
                    }
                }
            }
            catch
            {
                Disconnect("Listen Error");
            }
        }

        public void ListenLoopUnreliable()
        {
            try
            {
                while (true)
                {
                    byte[] bytes = UDPClient.Receive(ref remoteIpEndPoint);
                    int bytesRead = bytes.Length;

                    lock (readQueue)
                    {
                        for (int readPos = 0; readPos < bytesRead;)
                        {
                            byte[] packetLengthBytes = bytes[readPos..(readPos + 4)];
                            int packetLength = BitConverter.ToInt32(packetLengthBytes);

                            byte[] packetBytes = bytes[readPos..(readPos + packetLength)];

                            Packet packet = new Packet(packetBytes);

                            //Debug.Log("Listend Thread: Recieved unreliable packet " + packet.packetType + " from " + IP + ":" + port);

                            readQueue.Add(packet);

                            readPos += packetLength;
                        }
                    }
                }
            }
            catch
            {
                Disconnect("Listen Error");
            }
        }

        public void Disconnect(string reason = "unkown")
        {
            if (connectionMode == ConnectionMode.DISCONNECTED) return;

            Console.WriteLine("Disconnected connection " + IP + ":" + port + " because " + reason);

            connectionMode = ConnectionMode.DISCONNECTED;

            if (dataListenerThread != null && dataListenerThread.IsAlive) dataListenerThread.Interrupt();
            if (dataListenerUnreliableThread != null && dataListenerUnreliableThread.IsAlive) dataListenerUnreliableThread.Interrupt();
            if (dataSenderThread != null && dataSenderThread.IsAlive) dataSenderThread.Interrupt();

            if (TCPClient != null && networkStream != null && TCPClient.Connected && UDPClient != null)
            {
                TCPClient.Close();
                UDPClient.Close();
                networkStream.Close();
            }
        }

        static int FreeTcpPort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }
    }
}
