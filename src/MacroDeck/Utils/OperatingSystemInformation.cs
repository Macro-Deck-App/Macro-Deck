namespace SuchByte.MacroDeck.Utils;

public static class OperatingSystemInformation
{

    public static string GetWindowsVersionName()
    {
        var os = Environment.OSVersion;
        var version = os.Version;

        var versionString = "";

        switch (version.Major)
        {
            case 6:
                switch (version.Minor)
                {
                    case 0:
                        versionString = "Vista";
                        break;
                    case 1:
                        versionString = "7";
                        break;
                    case 2:
                        versionString = "8";
                        break;
                    case 3:
                        versionString = "8.1";
                        break;
                    default:
                        versionString = "unknown";
                        break;
                }
                break;
            case 10:
                if (os.Version.Build < 22000)
                {
                    versionString = "10";
                } else
                {
                    versionString = "11";
                }
                break;
        }

        return "Windows " + versionString + " build " + os.Version.Build + (Environment.Is64BitOperatingSystem ? " 64 bit" : " 32 bit");
    }

}