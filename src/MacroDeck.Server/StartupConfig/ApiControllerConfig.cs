using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Server.StartupConfig;

public static class ApiControllerConfig
{
    public static void RegisterRestApiControllers(this IServiceCollection services)
    {
        var assembly = typeof(MacroDeckServerHelper).Assembly;
        services.AddCors(
            options =>
            {
                options.AddPolicy(
                    "AllowAny",
                    builder =>
                        builder.SetIsOriginAllowed(_ => true)
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials()
                            .Build());
            });
        services.AddMvc();
        services.AddControllers()
            .AddJsonOptions(opt =>
            {
                var enumConverter = new JsonStringEnumConverter();
                opt.JsonSerializerOptions.Converters.Add(enumConverter);
                opt.JsonSerializerOptions.AllowTrailingCommas = true;
            })
            .PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
    }
}