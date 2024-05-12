using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using QRCoder;
using SuchByte.MacroDeck.DataTypes.QrCode;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.Services;

public class QrCodeService
{
    public static readonly QrCodeService Instance = new();

    private byte[]? _quickSetupQrCode;

    public Image GetQuickSetupQrCode()
    {
        if (_quickSetupQrCode is not null)
        {
            return FromSavedBytes();
        }

        var networkInterfaces = new List<string>();
        try
        {
            networkInterfaces.AddRange(NetworkInterface.GetAllNetworkInterfaces()
                .Select(adapter => adapter.GetIPProperties()
                    .UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)
                    ?.Address.ToString())
                .Where(address => !string.IsNullOrWhiteSpace(address) && address != "127.0.0.1"));
        }
        catch (Exception ex)
        {
            MacroDeckLogger.Warning($"Error while searching for network interfaces\n{ex.Message}");
        }

        var data = new QuickConnectQrCodeData(Environment.MachineName,
            networkInterfaces,
            MacroDeck.Configuration.HostPort,
            MacroDeck.Configuration.EnableSsl);

        var dataJson = JsonSerializer.Serialize(data);
        var dataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(dataJson));
        var qrCodeLink = $"https://macro-deck.app/quick-setup/{dataBase64}";
        
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(qrCodeLink, QRCodeGenerator.ECCLevel.L);
        using var qrCode = new BitmapByteQRCode(qrCodeData);

        _quickSetupQrCode = qrCode.GetGraphic(40);
        return FromSavedBytes();

        Image FromSavedBytes()
        {
            using var ms = new MemoryStream(_quickSetupQrCode);
            return Image.FromStream(ms);
        }
    }
}