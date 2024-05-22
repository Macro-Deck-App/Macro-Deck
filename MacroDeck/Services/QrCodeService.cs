using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using QRCoder;
using SuchByte.MacroDeck.DataTypes.QrCode;
using SuchByte.MacroDeck.Logging;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Services;

public class QrCodeService
{
    public static readonly QrCodeService Instance = new();

    public Image GetQuickSetupQrCode()
    {
        var networkInterfaces = NetworkUtils.GetNetworkInterfaces();

        var data = new QuickConnectQrCodeData(Environment.MachineName,
            networkInterfaces,
            MacroDeck.Configuration.HostPort,
            MacroDeck.Configuration.EnableSsl,
            MacroDeckServer.QuickSetupToken);

        var dataJson = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var dataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(dataJson));
        var qrCodeLink = $"https://macro-deck.app/quick-setup/{dataBase64}";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(qrCodeLink, QRCodeGenerator.ECCLevel.L);
        using var qrCode = new BitmapByteQRCode(qrCodeData);

        var quickSetupQrCode = qrCode.GetGraphic(15);
        using var ms = new MemoryStream(quickSetupQrCode);
        return Image.FromStream(ms);
    }
}