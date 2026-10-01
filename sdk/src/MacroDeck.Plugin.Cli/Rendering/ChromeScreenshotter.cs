using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MacroDeck.Plugin.Cli.Rendering;

internal sealed class ChromeScreenshotter : IPreviewScreenshotter
{
	private static readonly TimeSpan _shotTimeout = TimeSpan.FromSeconds(60);

	private const string Page =
		"<!doctype html><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"/preview-renderer.css\">" +
		"<div id=\"tile\"></div><script src=\"/preview-renderer.js\"></script>";

	private readonly string _browser;
	private readonly WebApplication _server;
	private readonly string _baseAddress;
	private readonly string _profileRoot = Path.Combine(Path.GetTempPath(), "macrodeck-preview-" + Guid.NewGuid().ToString("N"));
	private readonly ConcurrentDictionary<string, string> _scenes = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _status = new(StringComparer.Ordinal);
	private CdpConnection? _cdp;
	private string? _sessionId;
	private int _next;

	private ChromeScreenshotter(string browser, WebApplication server, string baseAddress)
	{
		_browser = browser;
		_server = server;
		_baseAddress = baseAddress;
	}

	public static async Task<ChromeScreenshotter> StartAsync(string browser, CancellationToken cancellationToken)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
		var server = builder.Build();
		ChromeScreenshotter? instance = null;

		var script = RendererAssets.Script;
		var style = RendererAssets.Style;

		server.MapGet("/render", () => Results.Content(Page, "text/html; charset=utf-8"));
		server.MapGet("/preview-renderer.js", () => Results.Bytes(script, "text/javascript"));
		server.MapGet("/preview-renderer.css", () => Results.Bytes(style, "text/css"));
		server.MapGet("/scene/{id}.json", (string id) => instance!._scenes.TryGetValue(id, out var json)
			? Results.Content(json, "application/json")
			: Results.NotFound());
		server.MapPost("/status/{id}", async (string id, HttpRequest request) =>
		{
			using var reader = new StreamReader(request.Body, Encoding.UTF8);
			var status = await reader.ReadToEndAsync().ConfigureAwait(false);

			if (instance!._status.TryGetValue(id, out var waiting))
			{
				waiting.TrySetResult(status);
			}

			return Results.Ok();
		});

		await server.StartAsync(cancellationToken).ConfigureAwait(false);
		var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
		instance = new ChromeScreenshotter(browser, server, address);

		return instance;
	}

	public async Task CaptureAsync(PreviewShot shot, CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _next).ToString(CultureInfo.InvariantCulture);
		var rendered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		_scenes[id] = shot.SceneJson;
		_status[id] = rendered;
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shot.OutputPath))!);

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(_shotTimeout);

		try
		{
			var cdp = await Connection(timeout.Token).ConfigureAwait(false);

			await cdp.SendAsync("Emulation.setDeviceMetricsOverride",
				new
				{
					width = shot.Size.Width,
					height = shot.Size.Height,
					deviceScaleFactor = shot.Scale,
					mobile = false
				},
				_sessionId,
				timeout.Token).ConfigureAwait(false);
			await cdp.SendAsync("Page.navigate", new { url = $"{_baseAddress}/render?id={id}" }, _sessionId, timeout.Token)
				.ConfigureAwait(false);

			var status = await rendered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

			if (status.StartsWith("unsupported", StringComparison.Ordinal))
			{
				throw new PreviewRenderException("preview-unsupported", status["unsupported".Length..].TrimStart(':', ' '));
			}

			if (status != "ready")
			{
				throw new PreviewRenderException("render-failed", status);
			}

			await cdp.SendAsync("Emulation.setDefaultBackgroundColorOverride",
				new { color = new { r = 0, g = 0, b = 0, a = 0 } },
				_sessionId,
				timeout.Token).ConfigureAwait(false);

			var result = await cdp.SendAsync("Page.captureScreenshot", new { format = "png" }, _sessionId, timeout.Token)
				.ConfigureAwait(false);
			await File.WriteAllBytesAsync(shot.OutputPath, Convert.FromBase64String(result.GetProperty("data").GetString()!),
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new PreviewRenderException("browser-timeout",
				$"The browser did not finish within {_shotTimeout.TotalSeconds:0} seconds.");
		}
		finally
		{
			_scenes.TryRemove(id, out _);
			_status.TryRemove(id, out _);
		}
	}

	private async Task<CdpConnection> Connection(CancellationToken cancellationToken)
	{
		if (_cdp is not null)
		{
			return _cdp;
		}

		_cdp = await CdpConnection.LaunchAsync(_browser, _profileRoot, cancellationToken).ConfigureAwait(false);
		var target = await _cdp.SendAsync("Target.createTarget", new { url = "about:blank" }, null, cancellationToken)
			.ConfigureAwait(false);
		var attached = await _cdp.SendAsync("Target.attachToTarget",
			new { targetId = target.GetProperty("targetId").GetString(), flatten = true },
			null,
			cancellationToken).ConfigureAwait(false);
		_sessionId = attached.GetProperty("sessionId").GetString();
		await _cdp.SendAsync("Page.enable", null, _sessionId, cancellationToken).ConfigureAwait(false);

		return _cdp;
	}

	public async ValueTask DisposeAsync()
	{
		if (_cdp is not null)
		{
			await _cdp.DisposeAsync().ConfigureAwait(false);
		}

		await _server.StopAsync().ConfigureAwait(false);
		await _server.DisposeAsync().ConfigureAwait(false);

		try
		{
			Directory.Delete(_profileRoot, recursive: true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}
}
