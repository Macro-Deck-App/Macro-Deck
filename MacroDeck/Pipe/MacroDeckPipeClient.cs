using System.IO.Pipes;

namespace SuchByte.MacroDeck.Pipes;

public class MacroDeckPipeClient
{

    internal static bool SendShowMainWindowMessage()
    {
        return SendPipeMessage("show");
    }


    internal static bool SendPipeMessage(string message)
    {
        var client = new NamedPipeClientStream("macrodeck");
        client.Connect(2000);
        if (client.IsConnected)
        {
            var buffer = Encoding.ASCII.GetBytes(message);
            client.Write(buffer, 0, buffer.Length);
            client.Close();
            client.Dispose();
            return true;
        }

        return false;
    }

}