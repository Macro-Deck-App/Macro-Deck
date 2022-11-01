using System;
using CommandLine;

namespace SuchByte.MacroDeck.Startup;

public class StartParameters
{

    [Option(Default = -1, Required = false)]
    public int Port { get; set; }

    [Option("force-update", Required = false)]
    public bool ForceUpdate { get; set; } 

    [Option("test-channel", Required = false)]
    public bool TestUpdateChannel { get; set; }

    [Option("export-default-strings", Required = false)]
    public bool ExportDefaultStrings { get; set; }

    [Option("portable", Required = false)] 
    public bool PortableMode { get; set; }

    [Option("show", Required = false)]
    public bool ShowMainWindow { get; set; }

    [Option("disable-file-logging", Required = false)]
    public bool DisableFileLogging { get; set; }

    [Option("log-level", Default = 1, Required = false)]
    public int LogLevel { get; set; }

    [Option("debug-console", Required = false)]
    public bool DebugConsole { get; set; }

    public static StartParameters ParseParameters(string[] args)
    {
        var startParameters = new StartParameters();
        if (args.Length == 0)
        {
            return startParameters;
        }

        using var parser = new Parser(options =>
        {
            options.HelpWriter = Console.Error;
            options.IgnoreUnknownArguments = true;
            options.EnableDashDash = true;
        });

        parser.ParseArguments<StartParameters>(args)
            .WithParsed(sp => startParameters = sp);

        return startParameters;
    }
}