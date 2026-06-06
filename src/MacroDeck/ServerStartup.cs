using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SuchByte.MacroDeck.StartupConfig;

namespace SuchByte.MacroDeck;

public class ServerStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.RegisterRestApiControllers();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseCors("AllowAny");
        if (MacroDeckServerHelper.UseHttps)
        {
            app.UseHttpsRedirection();
        }

        app.UseFileServer();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        });
        app.UseRouting();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
    }
}
