using System.Diagnostics;
using System;
using System.Text.RegularExpressions;

namespace SuchByte.MacroDeck.Models;

public class VersionModel
{
    private const string PatternBeta = @"^(\d+)\.(\d+)\.(\d+)-preview(\d+)$";
    private const string PatternRelease = @"^(\d+)\.(\d+)\.(\d+)$";
    
    public string VersionString { get; }

    public int BetaPatch { get; }

    public bool IsBeta { get; }

    public int Build { get; set; }

    public VersionModel(string? versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
        {
            throw new ArgumentNullException();
        }
        
        var matchBeta = Regex.Match(versionString, PatternBeta);
        var matchRelease = Regex.Match(versionString, PatternRelease);

        if (matchBeta.Success)
        {
            IsBeta = true;
            if (int.TryParse(matchBeta.Groups[4].Value, out var betaPatch))
            {
                BetaPatch = betaPatch;
            }
            VersionString = $"{matchBeta.Groups[1].Value}.{matchBeta.Groups[2].Value}.{matchBeta.Groups[3].Value}-preview{BetaPatch}" +
                            $"{(Debugger.IsAttached ? " (debug)" : "")}";
        } else if (matchRelease.Success)
        {
            VersionString = $"{matchBeta.Groups[1].Value}.{matchBeta.Groups[2].Value}.{matchBeta.Groups[3].Value} " +
                            $"{(Debugger.IsAttached ? " (debug)" : "")}";
        }
        else
        {
            VersionString = "Unknown version";
        }
    }
}