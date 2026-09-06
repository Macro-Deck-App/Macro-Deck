namespace MacroDeckHost.Application.Logging;

public static class LogNoiseCategories
{
	public static IReadOnlyList<string> RequestPipeline { get; } =
	[
		"Microsoft.AspNetCore.Hosting.Diagnostics",
		"Microsoft.AspNetCore.Routing.EndpointMiddleware",
		"Microsoft.AspNetCore.Mvc",
		"Microsoft.AspNetCore.Cors.Infrastructure.CorsService",
		"Microsoft.AspNetCore.StaticFiles"
	];
}
