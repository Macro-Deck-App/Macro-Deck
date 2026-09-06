using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace MacroDeck.Plugin.Hosting.Endpoints;

/// <summary>
/// Refuses any request under <see cref="ReservedPaths.Prefix" /> that is not one of the SDK's own
/// endpoints.
///
/// <para>
/// This is the second half of the reservation. The build-time endpoint scan catches an author who
/// <em>maps</em> a route under the prefix; it cannot see an author who writes middleware that answers
/// one, because middleware has no route to inspect. Installed through an <c>IStartupFilter</c>, this
/// runs before anything the author added, so their middleware never sees the request.
/// </para>
/// </summary>
internal sealed class ReservedPathStartupFilter : IStartupFilter
{
	public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
		=> app =>
		{
			app.Use(async (context, following) =>
			{
				var path = context.Request.Path.Value;

				if (ReservedPaths.IsReserved(path) && !IsSdkEndpoint(path))
				{
					context.Response.StatusCode = StatusCodes.Status404NotFound;
					return;
				}

				await following(context);
			});

			next(app);
		};

	private static bool IsSdkEndpoint(string? path)
		=> ReservedPaths.All.Any(route => string.Equals(route, path, StringComparison.OrdinalIgnoreCase));
}
