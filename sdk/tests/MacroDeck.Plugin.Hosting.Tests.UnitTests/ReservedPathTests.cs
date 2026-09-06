using System.Net;
using MacroDeck.Plugin.Hosting.Endpoints;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class ReservedPathTests
{
	[TestCase("/_macrodeck", ExpectedResult = true)]
	[TestCase("/_macrodeck/health", ExpectedResult = true)]
	[TestCase("/_macrodeck/anything/deeper", ExpectedResult = true)]
	[TestCase("_macrodeck/health", ExpectedResult = true)]
	[TestCase("/_macrodeckery", ExpectedResult = false)]
	[TestCase("/plugin/_macrodeck", ExpectedResult = false)]
	[TestCase("/", ExpectedResult = false)]
	[TestCase("", ExpectedResult = false)]
	public bool The_prefix_is_matched_on_segment_boundaries(string path) => ReservedPaths.IsReserved(path);

	[Test]
	public void Mapping_a_route_under_the_prefix_fails_the_build()
	{
		var exception = Assert.Throws<PluginConfigurationException>(() => MacroDeckPlugin.CreatePlugin()
			.Configure((_, app) => ((WebApplication)app).MapGet("/_macrodeck/mine", () => "no"))
			.Build());

		Assert.That(exception!.Problems, Has.One.Contains("/_macrodeck/mine"));
	}

	[Test]
	public void Mapping_a_route_beside_the_prefix_is_fine()
	{
		using var plugin = MacroDeckPlugin.CreatePlugin()
			.Configure((_, app) => ((WebApplication)app).MapGet("/_macrodeckery", () => "fine"))
			.Build();

		Assert.That(plugin.Metadata.Id, Is.EqualTo("com.example.test"));
	}

	[Test]
	public async Task Author_middleware_cannot_answer_a_reserved_path()
	{
		await using var plugin = MacroDeckPlugin.CreatePlugin()
			.Configure((_, app) => app.Use(async (context, next) =>
			{
				if (context.Request.Path.StartsWithSegments("/_macrodeck"))
				{
					// The endpoint scan cannot see this: middleware has no route to inspect. The
					// startup filter is what stops it, by running first.
					context.Response.StatusCode = StatusCodes.Status200OK;
					await context.Response.WriteAsync("hijacked");
					return;
				}

				await next(context);
			}))
			.Build();

		using var client = await StartAsync(plugin);
		using var response = await client.GetAsync(new Uri("/_macrodeck/not-an-sdk-route", UriKind.Relative));

		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[Test]
	public async Task Health_answers_before_a_session_exists_and_ready_does_not()
	{
		await using var plugin = MacroDeckPlugin.CreatePlugin().Build();

		using var client = await StartAsync(plugin);

		using var health = await client.GetAsync(new Uri(ReservedPaths.Health, UriKind.Relative));
		using var ready = await client.GetAsync(new Uri(ReservedPaths.Ready, UriKind.Relative));

		Assert.Multiple(() =>
		{
			Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
		});
	}

	[Test]
	public async Task Info_reports_the_plugin_and_its_registration_mode()
	{
		using var fixture = new PluginManifestFixture("""
													  {
													    "manifestVersion": 1,
													    "id": "com.example.test",
													    "name": "Test Plugin",
													    "version": "2.3.4"
													  }
													  """);

		await using var plugin = fixture.CreateBuilder().Build();

		using var client = await StartAsync(plugin);
		var info = await client.GetStringAsync(new Uri(ReservedPaths.Info, UriKind.Relative));

		Assert.Multiple(() =>
		{
			Assert.That(info, Does.Contain("com.example.test"));
			Assert.That(info, Does.Contain("Test Plugin"));
			Assert.That(info, Does.Contain("2.3.4"));
			Assert.That(info, Does.Contain("SelfRegistering"));
		});
	}

	[Test]
	public async Task Diagnostics_reports_the_connection_without_naming_a_credential()
	{
		await using var plugin = MacroDeckPlugin.CreatePlugin().Build();

		using var client = await StartAsync(plugin);
		var diagnostics = await client.GetStringAsync(new Uri(ReservedPaths.Diagnostics, UriKind.Relative));

		Assert.Multiple(() =>
		{
			Assert.That(diagnostics, Does.Contain("inFlightInvocations"));
			Assert.That(diagnostics.ToUpperInvariant(), Does.Not.Contain("SECRET"));
			Assert.That(diagnostics.ToUpperInvariant(), Does.Not.Contain("TOKEN"));
		});
	}

	/// <summary>
	/// Starts the plugin on a loopback port and hands back a client pointed at it. The builder binds
	/// port zero, so tests never collide with each other or with a running plugin.
	/// </summary>
	private static async Task<HttpClient> StartAsync(PluginApplication plugin)
	{
		await plugin.StartAsync();

		var address = plugin.WebApplication.Services.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!
			.Addresses.First();

		return new HttpClient { BaseAddress = new Uri(address) };
	}
}
