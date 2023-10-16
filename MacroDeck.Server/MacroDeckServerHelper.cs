using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace MacroDeck.Server;

public static class MacroDeckServerHelper
{
    private static IHost? _host;
    
    public static async Task Setup(int port)
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
                    options.ListenAnyIP(port);
                });
            }).Build();

        await _host.RunAsync();
    }
}