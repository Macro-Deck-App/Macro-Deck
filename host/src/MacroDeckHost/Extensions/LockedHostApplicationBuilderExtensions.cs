using System.Net;

namespace MacroDeckHost.Extensions;

public static class LockedHostApplicationBuilderExtensions
{
	/// <summary>
	/// The bootstrapper's readiness probes, plus what the unlock gate itself needs. Everything else under
	/// /api is refused while the key ring is locked, because it would be answering with a secret store
	/// the host cannot read.
	/// </summary>
	private static readonly string[] AllowedWhileLocked =
	[
		"/api/key-ring",

		// ui/bootstrapper/src/host.rs waits on these two and gives up with a blocking error dialog.
		"/api/system/version",
		"/api/auth/status",

		// Without this the bootstrapper's graceful stop is skipped and the host is killed after 5s.
		"/api/host/shutdown",

		// So the gate renders in the user's language rather than in key names.
		"/api/localization"
	];

	public static IApplicationBuilder UseLockedHostGate(this IApplicationBuilder app)
	{
		ArgumentNullException.ThrowIfNull(app);

		return app.Use(async (context, next) =>
		{
			if (!KeyRingStartupState.IsLocked ||
				!context.Request.Path.StartsWithSegments("/api") ||
				AllowedWhileLocked.Any(path => context.Request.Path.StartsWithSegments(path)))
			{
				await next();

				return;
			}

			context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
			context.Response.Headers["X-MacroDeck-Locked"] = "true";
			context.Response.Headers.CacheControl = "no-store";

			// A code, not a sentence: the client owns the wording, and it already has a translated one
			// for this state in every shipped language.
			await context.Response.WriteAsJsonAsync(new { error = "KeyRingLocked" });
		});
	}
}
