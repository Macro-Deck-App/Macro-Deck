using System.Diagnostics;

namespace SuchByte.MacroDeck.Models;

public class VersionModel
{
    public string VersionString { get; private set; }

    public int BetaPatch { get; private set; }

    public bool IsBeta { get; private set; }

    public int Build { get; private set; }


    public VersionModel(string versionString)
    {
        var versionArray = versionString.Split(".");
        IsBeta = versionString.Contains("b");
        if (IsBeta)
        {
            if(int.TryParse(versionString.Substring(versionString.IndexOf("b") + 1), out var betaRevision))
            {
                BetaPatch = betaRevision;
            }
            if (int.TryParse(versionArray[3].Substring(0, versionArray[3].IndexOf("b")), out var build))
            {
                Build = build;
            }
        } else
        {
            if (int.TryParse(versionArray[3], out var build))
            {
                Build = build;
            }
        }
        VersionString = $"{versionArray[0]}.{versionArray[1]}.{versionArray[2]}{(IsBeta ? $"b{BetaPatch}" : "")}{(Debugger.IsAttached ? " (debug)" : "")}";
            
    }


}