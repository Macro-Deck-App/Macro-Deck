using System.Diagnostics;
using Serilog;
using SuchByte.MacroDeck.Pipe;
using SuchByte.MacroDeck.StartupConfig;

namespace SuchByte.MacroDeck;

internal class Program
{
    private static readonly ILogger logger = Log.ForContext(typeof(Program));

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Register exception event handlers
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += ApplicationThreadException;

        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;


        var startParameters = StartParameters.ParseParameters(args);
        CheckRunningInstance(startParameters.IgnorePidCheck).Wait();

        ApplicationPaths.Initialize(startParameters.PortableMode);

        MacroDeck.Start(startParameters);
    }

    private static async Task CheckRunningInstance(int ignoredPid)
    {
        var proc = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(proc.ProcessName)
            .Where(x => ignoredPid == 0 || x.Id != ignoredPid)
            .ToArray();
        if (processes.Length <= 1)
        {
            return;
        }

        if (await MacroDeckPipeClient.SendShowMainWindowMessage())
        {
            Environment.Exit(0);
            return;
        }

        // Kill instance if no response
        foreach (var p in processes.Where(x => x.Id != proc.Id))
        {
            try
            {
                p.Kill();
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        logger.Error(e.ExceptionObject as Exception, "Unhandled domain exception: {ExceptionObject}", e.ExceptionObject);
    }

    private static void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
    {
        logger.Error(e.Exception, "Unhandled thread exception");
    }
}
