using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace SuchByte.MacroDeck.Pipes
{
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
                byte[] buffer = Encoding.ASCII.GetBytes(message);
                client.Write(buffer, 0, buffer.Length);
                client.Close();
                client.Dispose();
                return true;
            } else
            {
                return false;
            }
        }

    }
}
