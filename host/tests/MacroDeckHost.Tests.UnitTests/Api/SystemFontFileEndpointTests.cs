using System.Net;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Api;

[NonParallelizable]
public class SystemFontFileEndpointTests
{
	private const string TrueTypeFaceId = "stub-family-700-5-italic";
	private const string CffFaceId = "stub-family-400-5-upright";

	private IHost _host = null!;
	private HttpClient _client = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		var paths = new MacroDeckPaths();
		DatabaseMigrationHelper.MigrateDatabase(paths);

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<IStartupFilter, TrustedLoopbackStartupFilter>();
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
					services.RemoveAll<IFontCatalog>();
					services.AddSingleton<IFontCatalog>(new StubFontCatalog());
				});
			})
			.StartAsync();

		_client = _host.GetTestClient();
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, recursive: true);
		}
	}

	[TestCase("no-such-face-400-5-upright", TestName = "unknown face id")]
	[TestCase("%20", TestName = "whitespace face id")]
	[TestCase("..%2F..%2Fsecrets", TestName = "traversal face id")]
	public async Task Requesting_an_unservable_face_is_a_not_found_rather_than_a_server_error(string faceId)
	{
		var response = await _client.GetAsync($"/api/system/fonts/{faceId}/file");

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(response.Content.Headers.ContentType?.MediaType ?? string.Empty,
				Does.Not.StartWith("font/"),
				"a rejected face id must never be answered with font bytes");
		});
	}

	[Test]
	public async Task Requesting_a_known_face_serves_the_font_keyed_by_its_face_id()
	{
		var response = await _client.GetAsync($"/api/system/fonts/{TrueTypeFaceId}/file");
		var body = await response.Content.ReadAsByteArrayAsync();

		Assert.Multiple(() =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(body, Is.Not.Empty);
			Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("font/ttf"));
			Assert.That(response.Headers.ETag?.Tag, Is.EqualTo($"\"{TrueTypeFaceId}\""));
			Assert.That(response.Headers.CacheControl?.Public, Is.True);
			Assert.That(response.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromDays(365)));
		});
	}

	[Test]
	public async Task Serving_a_cff_face_reports_it_as_an_opentype_font()
	{
		var response = await _client.GetAsync($"/api/system/fonts/{CffFaceId}/file");

		Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("font/otf"));
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	private sealed class StubFontCatalog : IFontCatalog
	{
		private static readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal)
		{
			[TrueTypeFaceId] = [0x00, 0x01, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44],
			[CffFaceId] = [(byte)'O', (byte)'T', (byte)'T', (byte)'O', 0x11, 0x22, 0x33, 0x44]
		};

		public IReadOnlyList<FontFaceInfo> GetFaces() =>
		[
			new(TrueTypeFaceId, "Stub Family", 700, 5, "italic", "Bold Italic", true),
			new(CffFaceId, "Stub Family", 400, 5, "upright", "Regular", true)
		];

		public byte[]? GetFaceFile(string faceId) => Files.GetValueOrDefault(faceId);
	}

	private sealed class TrustedLoopbackStartupFilter : IStartupFilter
	{
		public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
			=> app =>
			{
				app.Use(async (context, nextMiddleware) =>
				{
					context.Connection.LocalPort = TestListenerPorts.Loopback;
					context.Connection.RemoteIpAddress = IPAddress.Loopback;
					await nextMiddleware();
				});
				next(app);
			};
	}
}
