namespace SuchByte.MacroDeck.DataTypes.QrCode;

public class QuickConnectQrCodeData
{
    public string InstanceName { get; set; }
    
    public string[] NetworkInterfaces { get; set; }

    public int Port { get; set; }

    public bool Ssl { get; set; }
    
    public string Token { get; set; }

    public QuickConnectQrCodeData(string instanceName, string[] networkInterfaces, int port, bool ssl, string token)
    {
        InstanceName = instanceName;
        NetworkInterfaces = networkInterfaces;
        Port = port;
        Ssl = ssl;
        Token = token;
    }
}