using Microsoft.Net.Http.Headers;

namespace MacroDeckHost.Extensions;

public static class SpaCachingApplicationBuilderExtensions
{
	public static IApplicationBuilder UseSpaShellNoCache(this IApplicationBuilder app)
	{
		return app.Use(async (context, next) =>
		{
			context.Response.OnStarting(() =>
			{
				var response = context.Response;
				if (StaticAssetCachePolicy.IsHtmlContentType(response.ContentType) &&
					!response.Headers.ContainsKey(HeaderNames.CacheControl))
				{
					response.Headers.CacheControl = StaticAssetCachePolicy.NoCache;
				}

				return Task.CompletedTask;
			});
			await next();
		});
	}

	public static StaticFileOptions CreateSpaStaticFileOptions()
	{
		return new StaticFileOptions
		{
			OnPrepareResponse = context =>
			{
				var cacheControl = StaticAssetCachePolicy.ResolveForFile(context.File.Name);
				if (cacheControl is not null)
				{
					context.Context.Response.Headers.CacheControl = cacheControl;
				}
			}
		};
	}
}
