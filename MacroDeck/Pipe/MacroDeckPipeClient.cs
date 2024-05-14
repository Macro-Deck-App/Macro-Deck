using System.IO.Pipes;

namespace SuchByte.MacroDeck.Pipe;

public static class MacroDeckPipeClient
{

    internal static Task<bool> SendShowMainWindowMessage()
    {
        return SendPipeMessage("show");
    }

    private static async Task<bool> SendPipeMessage(string message)
    {
        try
        {
            await using var client = new NamedPipeClientStream("macrodeck");
            await client.ConnectAsync(2000);
            if (client.IsConnected)
            {
                var bytes = Encoding.ASCII.GetBytes(message);
                client.Write(bytes, 0, bytes.Length);
                return true;
            }
        }
        catch (TimeoutException)
        {
            // Ignore   
        }
        
        return false;
    }

}