using System;
using System.Net;
using System.Net.Sockets;

class Program {
    static void Main() {
        var client = new UdpClient();
        client.Connect("127.0.0.1", 8080);
        byte[] data = { 12,0,0,0, 233,3,0,0, 234,3,0,0, 1,0,0,0, 0,0,0,0 };
        client.Send(data, data.Length);
        client.Close();
        Console.WriteLine("Sent UDP packet to 127.0.0.1:8080");
    }
}
