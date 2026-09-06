using System.Net;
using MacroDeckHost.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class ApiCachingIntegrationTests
{
	private IHost _host = null!;
	private HttpClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await new HostBuilder()
			.ConfigureWebHost(web => web
				.UseTestServer()
				.ConfigureServices(services => services.AddRouting())
				.Configure(app =>
				{
					app.UseApiNoStore();
					app.UseRouting();
					app.UseEndpoints(endpoints =>
					{
						endpoints.MapGet("/api/folders", () => Results.Json(new { folders = Array.Empty<int>() }));
						endpoints.MapGet("/api/icons/{id}/image",
							(HttpContext context) =>
							{
								context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
								return Results.Bytes([1, 2, 3], "image/webp");
							});
						endpoints.MapGet("/index.html", () => Results.Content("<!doctype html>", "text/html"));
					});
				}))
			.StartAsync();
		_client = _host.GetTestClient();
	}

	[TearDown]
	public async Task TearDown()
	{
		_client.Dispose();
		await _host.StopAsync();
		_host.Dispose();
	}

	[Test]
	public async Task Json_api_response_is_not_storable()
	{
		var response = await _client.GetAsync(new Uri("/api/folders", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That(CacheControlOf(response), Does.Contain("no-store"));
	}

	[Test]
	public async Task Media_endpoint_keeps_its_own_cache_policy()
	{
		var response = await _client.GetAsync(new Uri("/api/icons/abc/image", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var cacheControl = CacheControlOf(response);
		Assert.That(cacheControl, Does.Contain("immutable"));
		Assert.That(cacheControl, Does.Not.Contain("no-store"));
	}

	[Test]
	public async Task Non_api_response_is_left_alone()
	{
		var response = await _client.GetAsync(new Uri("/index.html", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That(CacheControlOf(response), Is.Empty);
	}

	private static string CacheControlOf(HttpResponseMessage response)
	{
		return response.Headers.TryGetValues(HeaderNames.CacheControl, out var values)
			? string.Join(", ", values)
			: string.Empty;
	}
}
