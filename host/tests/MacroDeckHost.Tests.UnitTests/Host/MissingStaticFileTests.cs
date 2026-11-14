using System.Net;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Host;

[TestFixture]
[NonParallelizable]
public class MissingStaticFileTests
{
	private const string Shell = "<!doctype html><html><body>web client</body></html>";
	private const string AdminShell = "<!doctype html><html><body>admin</body></html>";
	private const string TargetShell = "<!doctype html><html><body>target</body></html>";

	private string _webRoot = null!;
	private InProcessHost _host = null!;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_webRoot = Path.Combine(Path.GetTempPath(), "md-missing-file-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(_webRoot, "admin"));
		Directory.CreateDirectory(Path.Combine(_webRoot, "targets", "carthing"));
		await File.WriteAllTextAsync(Path.Combine(_webRoot, "index.html"), Shell);
		await File.WriteAllTextAsync(Path.Combine(_webRoot, "admin", "index.html"), AdminShell);
		await File.WriteAllTextAsync(Path.Combine(_webRoot, "targets", "carthing", "index.html"), TargetShell);
		await File.WriteAllTextAsync(Path.Combine(_webRoot, "admin", "main-ABCD2345.js"), "export {};");
		_host = await InProcessHost.StartAsync(webRoot: _webRoot);
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _host.DisposeAsync();
		try
		{
			Directory.Delete(_webRoot, true);
		}
		catch (IOException)
		{
		}
	}

	[TestCase("/admin/chunk-DOESNOTEXIST.js", FakeConnectionShape.PublicLan)]
	[TestCase("/admin/chunk-DOESNOTEXIST.js", FakeConnectionShape.Loopback)]
	[TestCase("/targets/carthing/chunk-DOESNOTEXIST.js", FakeConnectionShape.PublicLan)]
	[TestCase("/targets/carthing/chunk-DOESNOTEXIST.js", FakeConnectionShape.Loopback)]
	[TestCase("/chunk-DOESNOTEXIST.js", FakeConnectionShape.PublicLan)]
	[TestCase("/chunk-DOESNOTEXIST.js", FakeConnectionShape.Loopback)]
	[TestCase("/admin/assets/missing.css", FakeConnectionShape.PublicLan)]
	public async Task A_missing_file_is_not_found_for_an_anonymous_request(string path, FakeConnectionShape shape)
	{
		var response = await GetAsync(path, shape);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(response.Content.Headers.ContentType?.MediaType, Is.Not.EqualTo("text/html"));
			Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("<html"));
		});
	}

	[TestCase("/admin/", AdminShell)]
	[TestCase("/admin/some/route", AdminShell)]
	[TestCase("/targets/carthing/", TargetShell)]
	[TestCase("/targets/carthing/some/route", TargetShell)]
	[TestCase("/", Shell)]
	[TestCase("/some/route", Shell)]
	public async Task A_route_without_a_file_name_still_gets_its_shell(string path, string shell)
	{
		var response = await GetAsync(path, FakeConnectionShape.PublicLan);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
			Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(shell));
		});
	}

	[Test]
	public async Task An_existing_chunk_is_still_served()
	{
		var response = await GetAsync("/admin/main-ABCD2345.js", FakeConnectionShape.PublicLan);

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	private async Task<HttpResponseMessage> GetAsync(string path, FakeConnectionShape shape)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, path);
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, shape.ToString());
		return await _host.Client.SendAsync(request);
	}
}
