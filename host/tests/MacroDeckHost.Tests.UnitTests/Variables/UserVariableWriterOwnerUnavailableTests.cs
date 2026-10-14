using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class UserVariableWriterOwnerUnavailableTests
{
	private const string OwnerId = "com.example.mixer";
	private const string OwnerName = "Example Mixer";

	[Test]
	public async Task A_disabled_owner_is_named_by_its_display_name()
	{
		using var harness = await Harness.Create(new WritableProvider(), true);

		var result = await harness.Set("gain", "5");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Unavailable));
			Assert.That(result.Message,
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Errors.Variables.OwnerUnavailable(OwnerName))));
		});
	}

	[Test]
	public async Task The_message_follows_the_active_language()
	{
		using var harness = await Harness.Create(new WritableProvider(), true, "de");

		var result = await harness.Set("gain", "5");

		Assert.That(result.Message, Is.EqualTo($"{OwnerName} ist gerade nicht verfügbar"));
	}

	[Test]
	public async Task An_owner_that_is_no_longer_registered_is_named_by_its_id()
	{
		using var harness = await Harness.Create(null);

		var result = await harness.Set("gain", "5");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Unavailable));
			Assert.That(result.Message,
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Errors.Variables.OwnerUnavailable(OwnerId))));
		});
	}

	[Test]
	public async Task An_owner_that_reports_itself_unavailable_is_named_by_its_display_name()
	{
		var provider = new WritableProvider { Answer = VariableWriteResult.Unavailable("device unplugged") };
		using var harness = await Harness.Create(provider);

		var result = await harness.Set("gain", "5");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(UserVariableWriteStatus.Unavailable));
			Assert.That(result.Message,
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Errors.Variables.OwnerUnavailable(OwnerName))));
		});
	}

	private sealed class Harness : IDisposable
	{
		private readonly ServiceProvider _services;
		private readonly UserVariableWriter _writer;

		private Harness(ServiceProvider services)
		{
			_services = services;
			_writer = new UserVariableWriter(services.GetRequiredService<IServiceScopeFactory>());
		}

		public static async Task<Harness> Create(
			WritableProvider? provider,
			bool disabled = false,
			string culture = "en")
		{
			var integrations = new ConfigurableIntegrationRegistry(provider is null ? [] : [provider],
				disabled ? [OwnerId] : null);
			var variables = TestVariableServices.Create(new VariableRegistry(),
				new NullUserVariableStore(),
				new RecordingMediator(),
				integrations);
			var services = new ServiceCollection()
				.AddScoped<IVariableService>(_ => variables)
				.AddSingleton<IIntegrationRegistry>(integrations)
				.AddSingleton<VariableOwnerNames>()
				.AddSingleton(TestLocalization.Resolver)
				.AddSingleton<IAppPreferenceService>(new FakeLocalizationPreferences { Culture = culture })
				.BuildServiceProvider();

			await variables.CreateIntegrationVariable(OwnerId,
				"gain",
				VariableScope.Global,
				null,
				DomainVariableType.Numeric,
				1,
				0,
				"gain",
				new VariableDeclaration { Write = new VariableWriteCapability() });

			return new Harness(services);
		}

		public Task<UserVariableWriteResult> Set(string name, string value)
		{
			return _writer.ApplyAsync(name, null, UserVariableOperation.Set, value);
		}

		public void Dispose()
		{
			_writer.Dispose();
			_services.Dispose();
		}
	}

	private sealed class WritableProvider : IIntegration, IVariableProvider
	{
		public VariableWriteResult Answer { get; init; } = VariableWriteResult.Applied();

		public string Id => OwnerId;

		public LocalizedText Name => OwnerName;

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public IReadOnlyList<VariableDefinition> Variables =>
		[
			VariableDefinition.Eager("gain", SdkVariableType.Numeric) with
			{
				Id = "gain", Write = new VariableWriteCapability()
			}
		];

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		public ValueTask<VariableWriteResult> SetValueAsync(
			string localId,
			object? value,
			CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(Answer);
		}

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}
}
