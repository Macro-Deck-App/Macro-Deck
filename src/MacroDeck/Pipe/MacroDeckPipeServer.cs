using System.IO;
using System.IO.Pipes;
using Serilog;

namespace SuchByte.MacroDeck.Pipe;

public class MacroDeckPipeServer
{
    private static readonly ILogger logger = Log.ForContext(typeof(MacroDeckPipeServer));

    public static event Action<string>? PipeMessage;

    public static void Initialize()
    {
        logger.Information("Initializing pipe server");
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
                logger.Error(ex, "Failed to handle pipe connection");
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
