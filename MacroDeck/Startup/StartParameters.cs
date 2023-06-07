using CommandLine;

namespace SuchByte.MacroDeck.Startup;

public class StartParameters
{

    [Option("port", Default = -1, Required = false)]
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

    [Option("ignore-pid-check", Default = 0, Required = false)]
    public int IgnorePidCheck { get; set; }

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

    public static string[] ToArray(StartParameters startParameters)
    {
        var parameters = new string[startParameters.GetType().GetProperties().Length];
        var properties = startParameters.GetType().GetProperties();

        for (var i = 0; i < properties.Length; i++)
        {
            var propertyInfo = properties[i];
            var optionAttribute = Attribute.GetCustomAttribute(propertyInfo, typeof(OptionAttribute)) as OptionAttribute;
            if (optionAttribute == null) continue;
            parameters[i] = $"--{optionAttribute.LongName} " + propertyInfo.GetValue(startParameters);
        }

        return parameters;
    }
}