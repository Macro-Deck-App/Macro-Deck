using System.Net;
using MacroDeckHost.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class SpaCachingIntegrationTests
{
	private string _root = null!;
	private IHost _host = null!;
	private HttpClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "md-spa-cache-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
		await File.WriteAllTextAsync(Path.Combine(_root, "index.html"), "<!doctype html><html><body></body></html>");
		await File.WriteAllTextAsync(Path.Combine(_root, "main-ABCD2345.js"), "export const value = 1;");
		await File.WriteAllTextAsync(Path.Combine(_root, "ngsw.json"), "{}");

		var provider = new PhysicalFileProvider(_root);
		_host = await new HostBuilder()
			.ConfigureWebHost(web => web
				.UseTestServer()
				.ConfigureServices(services => services.AddRouting())
				.Configure(app =>
				{
					app.UseSpaShellNoCache();

					var staticOptions = SpaCachingApplicationBuilderExtensions.CreateSpaStaticFileOptions();
					staticOptions.FileProvider = provider;
					app.UseStaticFiles(staticOptions);

					app.UseRouting();
					app.UseEndpoints(endpoints =>
						endpoints.MapFallbackToFile("{**path}",
							"index.html",
							new StaticFileOptions { FileProvider = provider }));
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
		try
		{
			Directory.Delete(_root, true);
		}
		catch (IOException)
		{
		}
	}

	[Test]
	public async Task Index_html_is_served_with_no_cache()
	{
		var response = await _client.GetAsync(new Uri("/index.html", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var cacheControl = CacheControlOf(response);
		Assert.That(cacheControl, Does.Contain("no-cache"));
		Assert.That(cacheControl, Does.Contain("no-store"));
	}

	[Test]
	public async Task Fingerprinted_asset_is_served_immutable()
	{
		var response = await _client.GetAsync(new Uri("/main-ABCD2345.js", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var cacheControl = CacheControlOf(response);
		Assert.That(cacheControl, Does.Contain("immutable"));
		Assert.That(cacheControl, Does.Contain("max-age=31536000"));
	}

	[Test]
	public async Task Ngsw_json_is_served_with_no_cache()
	{
		var response = await _client.GetAsync(new Uri("/ngsw.json", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var cacheControl = CacheControlOf(response);
		Assert.That(cacheControl, Does.Contain("no-cache"));
		Assert.That(cacheControl, Does.Contain("no-store"));
	}

	[Test]
	public async Task Missing_chunk_falls_back_to_index_html_with_no_cache()
	{
		var response = await _client.GetAsync(new Uri("/chunk-DOESNOTEXIST.js", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
		Assert.That(CacheControlOf(response), Does.Contain("no-cache"));
	}

	private static string CacheControlOf(HttpResponseMessage response)
	{
		return response.Headers.TryGetValues("Cache-Control", out var values)
			? string.Join(", ", values)
			: string.Empty;
	}
}
