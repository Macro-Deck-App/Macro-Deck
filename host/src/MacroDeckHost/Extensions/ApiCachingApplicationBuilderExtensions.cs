using Microsoft.Net.Http.Headers;

namespace MacroDeckHost.Extensions;

public static class ApiCachingApplicationBuilderExtensions
{
	public const string NoStore = "no-store";

	private const string ApiPrefix = "/api";

	public static IApplicationBuilder UseApiNoStore(this IApplicationBuilder app)
	{
		return app.Use(async (context, next) =>
		{
			if (context.Request.Path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase))
			{
				context.Response.OnStarting(() =>
				{
					var response = context.Response;
					if (!response.Headers.ContainsKey(HeaderNames.CacheControl))
					{
						response.Headers.CacheControl = NoStore;
					}

					return Task.CompletedTask;
				});
			}

			await next();
		});
	}
}
