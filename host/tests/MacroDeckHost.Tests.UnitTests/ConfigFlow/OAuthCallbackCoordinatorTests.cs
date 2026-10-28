using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
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

	[TestCase("attacker-code", null)]
	[TestCase(null, "access_denied")]
	public async Task A_state_that_received_a_code_ignores_every_later_callback(string? laterCode, string? laterError)
	{
		using var logger = new LoggerConfiguration().CreateLogger();
		var transport = new RecordingUiTransport();
		var coordinator = new OAuthCallbackCoordinator(transport, logger);
		var registration = coordinator.Register(Guid.NewGuid());

		var first = await coordinator.HandleCallbackAsync(registration.State,
			"genuine-code",
			null,
			CancellationToken.None);
		var second = await coordinator.HandleCallbackAsync(registration.State,
			laterCode,
			laterError,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.True);
			Assert.That(second, Is.False);
			Assert.That(coordinator.GetCode(registration.State), Is.EqualTo("genuine-code"));
			Assert.That(transport.Broadcasts.OfType<ConfigFlowAuthorizedNotification>().ToList(),
				Has.Count.EqualTo(1).And.All.Matches<ConfigFlowAuthorizedNotification>(n => n.Success));
		});
	}

	[Test]
	public async Task A_failed_callback_leaves_the_state_open_for_another_attempt()
	{
		using var logger = new LoggerConfiguration().CreateLogger();
		var coordinator = new OAuthCallbackCoordinator(new RecordingUiTransport(), logger);
		var registration = coordinator.Register(Guid.NewGuid());

		var failed = await coordinator.HandleCallbackAsync(registration.State,
			null,
			"access_denied",
			CancellationToken.None);
		var retried = await coordinator.HandleCallbackAsync(registration.State, "code", null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(failed, Is.True);
			Assert.That(retried, Is.True);
			Assert.That(coordinator.GetCode(registration.State), Is.EqualTo("code"));
		});
	}

	[Test]
	public async Task A_new_authorization_attempt_accepts_its_own_code()
	{
		using var logger = new LoggerConfiguration().CreateLogger();
		var coordinator = new OAuthCallbackCoordinator(new RecordingUiTransport(), logger);
		var registration = coordinator.Register(Guid.NewGuid());
		await coordinator.HandleCallbackAsync(registration.State, "spent-code", null, CancellationToken.None);

		coordinator.ExpectCallback(registration.State);
		var handled = await coordinator.HandleCallbackAsync(registration.State,
			"fresh-code",
			null,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(handled, Is.True);
			Assert.That(coordinator.GetCode(registration.State), Is.EqualTo("fresh-code"));
		});
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
		{
			return Task.CompletedTask;
		}

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
		{
			return Task.CompletedTask;
		}

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
		{
			return Task.CompletedTask;
		}
	}
}
