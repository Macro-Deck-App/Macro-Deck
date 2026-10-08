using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpSettingsServiceTests
{
	private sealed class FakeAppPreferenceRepository : IAppPreferenceRepository
	{
		private readonly Dictionary<string, AppPreferenceEntity> _store = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(_store.GetValueOrDefault(key));

		public Task SetValue(string key, string value)
		{
			_store[key] = new AppPreferenceEntity { Key = key, Value = value };
			return Task.CompletedTask;
		}
	}

	private FakeAppPreferenceRepository _repository = null!;
	private HttpUserAgentState _state = null!;
	private HttpSettingsService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_repository = new FakeAppPreferenceRepository();
		_state = new HttpUserAgentState();
		_service = new HttpSettingsService(_repository, _state);
	}

	[Test]
	public async Task Nothing_stored_reports_the_default_as_effective()
	{
		var settings = await _service.Load();

		Assert.Multiple(() =>
		{
			Assert.That(settings.CustomUserAgent, Is.Null);
			Assert.That(settings.EffectiveUserAgent, Is.EqualTo(HttpUserAgent.Default));
			Assert.That(settings.DefaultUserAgent, Is.EqualTo(HttpUserAgent.Default));
		});
	}

	[Test]
	public async Task A_custom_value_is_persisted_and_applied_to_the_live_state()
	{
		var update = await _service.SetUserAgent("Custom/2.0");

		Assert.Multiple(() =>
		{
			Assert.That(update.Success, Is.True);
			Assert.That(update.Settings.EffectiveUserAgent, Is.EqualTo("Custom/2.0"));
			Assert.That(_state.Current, Is.EqualTo("Custom/2.0"));
		});
	}

	[Test]
	public async Task A_custom_value_survives_a_restart()
	{
		await _service.SetUserAgent("Custom/2.0");
		var restartedState = new HttpUserAgentState();
		var restarted = new HttpSettingsService(_repository, restartedState);

		await restarted.Seed();
		var reloaded = await restarted.Load();

		Assert.Multiple(() =>
		{
			Assert.That(restartedState.Current, Is.EqualTo("Custom/2.0"));
			Assert.That(reloaded.CustomUserAgent, Is.EqualTo("Custom/2.0"));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public async Task Restoring_the_default_clears_the_custom_value_in_store_and_state(string? blank)
	{
		await _service.SetUserAgent("Custom/2.0");

		var update = await _service.SetUserAgent(blank);
		var restartedState = new HttpUserAgentState();
		await new HttpSettingsService(_repository, restartedState).Seed();

		Assert.Multiple(() =>
		{
			Assert.That(update.Success, Is.True);
			Assert.That(update.Settings.CustomUserAgent, Is.Null);
			Assert.That(_state.Current, Is.EqualTo(HttpUserAgent.Default));
			Assert.That(restartedState.Current, Is.EqualTo(HttpUserAgent.Default));
		});
	}

	[Test]
	public async Task An_invalid_value_is_rejected_and_the_previous_value_stays_in_force()
	{
		await _service.SetUserAgent("Custom/2.0");

		var update = await _service.SetUserAgent("Bad/1.0\r\nX-Injected: yes");
		var reloaded = await _service.Load();

		Assert.Multiple(() =>
		{
			Assert.That(update.Success, Is.False);
			Assert.That(update.Settings.EffectiveUserAgent, Is.EqualTo("Custom/2.0"));
			Assert.That(_state.Current, Is.EqualTo("Custom/2.0"));
			Assert.That(reloaded.CustomUserAgent, Is.EqualTo("Custom/2.0"));
		});
	}

	[Test]
	public async Task The_update_handler_reports_an_invalid_value_as_an_error_without_changing_anything()
	{
		var handler = new UpdateHttpSettingsRequestMessageHandler(_service);

		var response = await handler.Handle(new UpdateHttpSettingsRequest { UserAgent = "(unbalanced" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo("InvalidUserAgent"));
			Assert.That(response.EffectiveUserAgent, Is.EqualTo(HttpUserAgent.Default));
		});
	}

	[Test]
	public async Task The_get_handler_reports_custom_effective_and_default_values()
	{
		await _service.SetUserAgent("Custom/2.0");
		var handler = new GetHttpSettingsRequestMessageHandler(_service);

		var response = await handler.Handle(new GetHttpSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.CustomUserAgent, Is.EqualTo("Custom/2.0"));
			Assert.That(response.EffectiveUserAgent, Is.EqualTo("Custom/2.0"));
			Assert.That(response.DefaultUserAgent, Is.EqualTo(HttpUserAgent.Default));
		});
	}
}
