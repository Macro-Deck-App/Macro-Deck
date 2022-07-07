using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SuchByte.MacroDeck.Models
{
    public class VersionModel
    {
        public string VersionString { get; private set; }

        public int BetaPatch { get; private set; } = 0;

        public bool IsBeta { get; private set; } = false;

        public int Build { get; private set; }


        public VersionModel(string versionString)
        {
            string[] versionArray = versionString.Split(".");
            IsBeta = versionString.Contains("b");
            if (IsBeta)
            {
                if(Int32.TryParse(versionString.Substring(versionString.IndexOf("b") + 1), out int betaRevision))
                {
                    this.BetaPatch = betaRevision;
                }
                if (Int32.TryParse(versionArray[3].Substring(0, versionArray[3].IndexOf("b")), out int build))
                {
                    this.Build = build;
                }
            } else
            {
                if (Int32.TryParse(versionArray[3], out int build))
                {
                    this.Build = build;
                }
            }
            VersionString = $"{versionArray[0]}.{versionArray[1]}.{versionArray[2]}{(IsBeta ? $"b{BetaPatch}" : "")}{(Debugger.IsAttached ? " (debug)" : "")}";
            
        }


    }
}
