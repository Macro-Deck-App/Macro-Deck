using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Pipes
{

    public delegate void DelegateMessage(string message);
    public class MacroDeckPipeServer
    {
        public static event DelegateMessage PipeMessage;


        private static void WaitForConnectionCallBack(IAsyncResult iar)
        {
            try
            {
                using (var pipeServer = (NamedPipeServerStream)iar.AsyncState)
                {
                    pipeServer.EndWaitForConnection(iar);
                    byte[] buffer = new byte[255];
                    pipeServer.Read(buffer, 0, 255);
                    string stringData = Encoding.ASCII.GetString(buffer).Trim('\0');
                    Task.Run(() => PipeMessage.Invoke(stringData));
                    pipeServer.Close();
                    SpawnServerStream();
                }
            }
            catch
            {
                return;
            }
        }

        private static void SpawnServerStream()
        {
            try
            {
                MacroDeckLogger.Trace(typeof(MacroDeckPipeServer), $"Spawning new server stream...");
                NamedPipeServerStream pipeServer = new NamedPipeServerStream("macrodeck", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                pipeServer.BeginWaitForConnection(new AsyncCallback(WaitForConnectionCallBack), pipeServer);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(typeof(MacroDeckPipeServer), $"Failed: {ex.Message}");
            }
        }

        public static void Initialize()
        {
            MacroDeckLogger.Info(typeof(MacroDeckPipeServer), $"Initializing pipe server");
            SpawnServerStream();
            MacroDeckLogger.Info(typeof(MacroDeckPipeServer), $"Initializing pipe server complete");
        }
    }
}
