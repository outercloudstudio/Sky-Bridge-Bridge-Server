﻿using System;
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

        private static int bufferSize = 4096;

        static void Main(string[] args)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            while (true)
            {
                Console.WriteLine("Waiting for connection...");
                TcpClient client = listener.AcceptTcpClient();

                Console.WriteLine("Connection accepted!");
                NetworkStream networkStream = client.GetStream();

                byte[] sendBuffer = new Packet(Packet.PacketType.DEBUG_PACKET).AddValue("Debug!").ToBytes();

                networkStream.Write(sendBuffer, 0, sendBuffer.Length);

                //SkyBridge.me.connections.Add(new Connection(client, networkStream, client.Client.RemoteEndPoint.ToString(), false));
            }
        }
    }
}
