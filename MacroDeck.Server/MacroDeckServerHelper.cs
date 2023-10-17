using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;

namespace MacroDeck.Server;

public static class MacroDeckServerHelper
{
    private static IHost? _host;
    
    public static async Task Setup(int port, bool enableSsl, string? certificatePath, string? certificatePassword)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
        }
        
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(hostBuilder =>
            {
                hostBuilder.UseStartup<Startup>();
                hostBuilder.ConfigureKestrel(options =>
                {
                    if (enableSsl && !string.IsNullOrWhiteSpace(certificatePath))
                    {
                        options.ListenAnyIP(port, listenOptions =>
                        {
                            listenOptions.UseHttps(new X509Certificate2(certificatePath, certificatePassword));
                        });
                    } else
                    {
                        options.ListenAnyIP(port);
                    }
                });
            }).Build();

        await _host.RunAsync();
    }
}