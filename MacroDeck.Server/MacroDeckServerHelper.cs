using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace MacroDeck.Server;

public static class MacroDeckServerHelper
{
    private static IHost? _host;
    
    internal static bool UseHttps { get; private set; }
    
    public static async Task Setup(int port, bool enableSsl, string? certificatePath, string? certificatePassword)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
        }

        UseHttps = enableSsl && !string.IsNullOrWhiteSpace(certificatePath);
        
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(hostBuilder =>
            {
                hostBuilder.UseStartup<Startup>();
                hostBuilder.ConfigureKestrel(options =>
                {
                    if (UseHttps)
                    {
                        options.ListenAnyIP(port, listenOptions =>
                        {
                            listenOptions.UseHttps(new X509Certificate2(certificatePath, certificatePassword));
                            listenOptions.Protocols = HttpProtocols.Http1;
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