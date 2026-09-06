using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.ConfigFlow;

public class OAuthCallbackCoordinatorTests
{
	[Test]
	public void RedirectUri_uses_the_build_configured_public_port()
	{
		using var logger = new LoggerConfiguration().CreateLogger();
		var coordinator = new OAuthCallbackCoordinator(new NoOpUiTransport(), logger);

		Assert.That(coordinator.RedirectUri,
			Is.EqualTo($"http://127.0.0.1:{BuildConfig.PublicPort}{OAuthCallbackCoordinator.CallbackPath}"));
	}

	// The URI a provider has to accept changes with the configured port, so it must not keep
	// reporting the build default after startup resolved something else.
	[Test]
	[NonParallelizable]
	public void RedirectUri_follows_a_configured_public_port()
	{
		AssertRedirectUri(PublicEndpointSet.HttpOnly(9100), $"http://127.0.0.1:9100");
	}

	[Test]
	[NonParallelizable]
	public void RedirectUri_stays_on_http_when_https_runs_on_an_additional_port()
	{
		AssertRedirectUri(PublicEndpointSet.HttpAndHttps(9100, 9101), "http://127.0.0.1:9100");
	}

	[Test]
	[NonParallelizable]
	public void RedirectUri_becomes_https_when_https_replaced_the_http_listener()
	{
		AssertRedirectUri(PublicEndpointSet.HttpsReplacingHttp(9100), "https://127.0.0.1:9100");
	}

	private static void AssertRedirectUri(PublicEndpointSet endpoints, string expectedPrefix)
	{
		using var logger = new LoggerConfiguration().CreateLogger();
		var coordinator = new OAuthCallbackCoordinator(new NoOpUiTransport(), logger);

		ResolvedPublicEndpoints.ResetForTests();
		ResolvedPublicEndpoints.Set(endpoints);
		try
		{
			Assert.That(coordinator.RedirectUri,
				Is.EqualTo($"{expectedPrefix}{OAuthCallbackCoordinator.CallbackPath}"));
		}
		finally
		{
			ResolvedPublicEndpoints.ResetForTests();
		}
	}

	private sealed class NoOpUiTransport : IUiTransport
	{
		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;
	}
}
