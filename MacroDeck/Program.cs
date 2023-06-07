﻿using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Pipes;
using SuchByte.MacroDeck.Startup;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace SuchByte.MacroDeck;

internal class Program
{

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
        CheckRunningInstance(startParameters.IgnorePidCheck);
        
        ApplicationPaths.Initialize(startParameters.PortableMode);
        CefSetup.Initialize();
        
        MacroDeck.Start(startParameters);
    }
    
    private static void CheckRunningInstance(int ignoredPid)
    {
        var proc = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(proc.ProcessName).Where(x => ignoredPid == 0 || x.Id != ignoredPid).ToArray();
        if (processes?.Length <= 1) return;
        if (MacroDeckPipeClient.SendShowMainWindowMessage())
        {
            Environment.Exit(0);
            return;
        }

        // Kill instance if no response
        foreach (var p in processes?.Where(x => x.Id != proc.Id) ?? Array.Empty<Process>())
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
        MacroDeckLogger.Error(typeof(MacroDeck), "CurrentDomainOnUnhandledException: " + e.ExceptionObject);
    }

    private static void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
    {
        MacroDeckLogger.Error(typeof(MacroDeck), "ApplicationThreadException: " + e.Exception.Message + Environment.NewLine + e.Exception.StackTrace);
    }
}