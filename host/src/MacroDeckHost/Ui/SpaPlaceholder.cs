using System.Net.Mime;

namespace MacroDeckHost.Ui;

internal static class SpaPlaceholder
{
	public static bool IsClientMissing(string? webRootPath)
		=> string.IsNullOrEmpty(webRootPath) || !File.Exists(Path.Combine(webRootPath, "index.html"));

	public static Task Handle(HttpContext context)
	{
		context.Response.StatusCode = StatusCodes.Status200OK;
		context.Response.ContentType = MediaTypeNames.Text.Html;
		return context.Response.WriteAsync(Page);
	}

	private const string Page = """
								<!doctype html>
								<html lang="en">
								<head>
									<meta charset="utf-8">
									<meta name="viewport" content="width=device-width, initial-scale=1">
									<title>Macro Deck host</title>
									<style>
										body { margin: 0; min-height: 100vh; display: grid; place-items: center;
											font-family: system-ui, sans-serif; background: #14161a; color: #e6e8eb; }
										main { max-width: 32rem; padding: 2rem; }
										h1 { margin: 0 0 .5rem; font-size: 1.25rem; }
										p { margin: 0 0 .75rem; line-height: 1.5; color: #a8adb5; }
										code { background: #22262c; border-radius: .25rem; padding: .1rem .35rem; color: #e6e8eb; }
										.ok { color: #3ddc84; font-weight: 600; }
									</style>
								</head>
								<body>
									<main>
										<h1><span class="ok">Connected.</span> This host is reachable.</h1>
										<p>You have reached the Macro Deck host, so the connection itself works. If you got here
											from a phone over USB, the tunnel is up.</p>
										<p>There is no interface to show because this is a development build: the host serves only
											its API, and the web client has not been built into it. A packaged build serves the
											real client from this address.</p>
										<p>The API is live at <code>/api/system/build-info</code>.</p>
									</main>
								</body>
								</html>
								""";
}
