namespace SuchByte.MacroDeck.DataTypes.QrCode;

public class QuickConnectQrCodeData
{
    public string InstanceName { get; set; }
    
    public List<string> NetworkInterfaces { get; set; }

    public int Port { get; set; }

    public bool Ssl { get; set; }

    public QuickConnectQrCodeData(string instanceName, List<string> networkInterfaces, int port, bool ssl)
    {
        InstanceName = instanceName;
        NetworkInterfaces = networkInterfaces;
        Port = port;
        Ssl = ssl;
    }
}