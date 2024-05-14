using System.IO;
using System.IO.Pipes;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Pipe;

public class MacroDeckPipeServer
{
    public static event Action<string>? PipeMessage;

    public static void Initialize()
    {
        MacroDeckLogger.Info(typeof(MacroDeckPipeServer), "Initializing pipe server");
        Task.Run(async () => await HandleConnections().ConfigureAwait(false));
    }

    private static async Task HandleConnections()
    {
        do
        {
            await using var pipeServer = new NamedPipeServerStream("macrodeck", PipeDirection.InOut, 1);
            using var sr = new StreamReader(pipeServer);
            try
            {
                await pipeServer.WaitForConnectionAsync();
                pipeServer.WaitForPipeDrain();
                var result = await sr.ReadLineAsync();
                PipeMessage?.Invoke(result);
            }
            catch (Exception ex)
            {
                MacroDeckLogger.Error(typeof(MacroDeckPipeServer), $"Failed: {ex.Message}");
            }
            finally
            {
                if (pipeServer.IsConnected)
                {
                    pipeServer.Disconnect();
                }
            }
        } while (true);
    }
}