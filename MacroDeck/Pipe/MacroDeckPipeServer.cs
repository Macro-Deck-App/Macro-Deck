using System.IO.Pipes;
using System.Threading;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Pipes;

public delegate void DelegateMessage(string message);
public class MacroDeckPipeServer
{
    public static event DelegateMessage PipeMessage;


    private static void WaitForConnectionCallBack(IAsyncResult iar)
    {
        try
        {
            using var pipeServer = (NamedPipeServerStream)iar.AsyncState;
            pipeServer.EndWaitForConnection(iar);
            var buffer = new byte[255];
            pipeServer.Read(buffer, 0, 255);
            var stringData = Encoding.ASCII.GetString(buffer).Trim('\0');
            var t = new Thread(() => PipeMessage.Invoke(stringData));
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            pipeServer.Close();
            SpawnServerStream();
        }
        catch
        {
        }
    }

    private static void SpawnServerStream()
    {
        try
        {
            MacroDeckLogger.Trace(typeof(MacroDeckPipeServer), "Spawning new server stream...");
            var pipeServer = new NamedPipeServerStream("macrodeck", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            pipeServer.BeginWaitForConnection(WaitForConnectionCallBack, pipeServer);
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Error(typeof(MacroDeckPipeServer), $"Failed: {ex.Message}");
        }
    }

    public static void Initialize()
    {
        MacroDeckLogger.Info(typeof(MacroDeckPipeServer), "Initializing pipe server");
        SpawnServerStream();
        MacroDeckLogger.Info(typeof(MacroDeckPipeServer), "Initializing pipe server complete");
    }
}