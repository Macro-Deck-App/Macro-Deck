using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
internal sealed class CachingInitializeBackgroundServiceTests
{
	[Test]
	public async Task ExecuteWhenReady_NoDefaultProfileCreated_WhenProfilesExistedButWereUnreadable()
	{
		var profileStore = new InMemoryProfileStore { UnreadableCount = 1 };
		var profileService = new RecordingProfileService();
		var readiness = new StartupReadiness();
		var service = CreateService(profileStore, profileService, readiness);

		await service.StartAsync(CancellationToken.None);
		if (service.ExecuteTask is { } executeTask)
		{
			await executeTask;
		}

		await service.StopAsync(CancellationToken.None);
		readiness.MarkVariablesReady();

		Assert.Multiple(() =>
		{
			Assert.That(profileService.CreatedNames, Is.Empty);
			Assert.That(readiness.IsReady, Is.True, "startup must complete even when profiles were unreadable");
		});
	}

	[Test]
	public async Task ExecuteWhenReady_CreatesDefaultProfile_WhenTheProfilesDirectoryIsGenuinelyEmpty()
	{
		var profileStore = new InMemoryProfileStore();
		var profileService = new RecordingProfileService();
		var readiness = new StartupReadiness();
		var service = CreateService(profileStore, profileService, readiness);

		await service.StartAsync(CancellationToken.None);
		if (service.ExecuteTask is { } executeTask)
		{
			await executeTask;
		}

		await service.StopAsync(CancellationToken.None);
		readiness.MarkVariablesReady();

		Assert.Multiple(() =>
		{
			Assert.That(profileService.CreatedNames, Has.Count.EqualTo(1));
			Assert.That(profileService.CreatedNames, Has.One.EqualTo("Default Profile"));
			Assert.That(readiness.IsReady, Is.True);
		});
	}

	[Test]
	public async Task ExecuteWhenReady_NamesTheDefaultProfileInTheActiveLanguage()
	{
		var profileService = new RecordingProfileService();
		var readiness = new StartupReadiness();
		var preferences = (FakeLocalizationPreferences)TestLocalization.Preferences;
		var previous = preferences.Culture;
		preferences.Culture = "de";

		try
		{
			var service = CreateService(new InMemoryProfileStore(), profileService, readiness);

			await service.StartAsync(CancellationToken.None);
			if (service.ExecuteTask is { } executeTask)
			{
				await executeTask;
			}

			await service.StopAsync(CancellationToken.None);
			readiness.MarkVariablesReady();
		}
		finally
		{
			preferences.Culture = previous;
		}

		// Text, not a reference: whoever renames it later renames a string, and the name it was born
		// with cannot silently change out from under them when the language does.
		Assert.That(profileService.CreatedNames, Has.One.EqualTo("Standardprofil"));
	}

	private static CachingInitializeBackgroundService CreateService(
		InMemoryProfileStore profileStore,
		RecordingProfileService profileService,
		StartupReadiness readiness)
	{
		var profileCache = new ProfileCache(profileStore, Log.Logger);
		var folderCache = new StubFolderCache();
		var scriptCache = new ScriptCache(new InMemoryScriptStore(), Log.Logger);
		var automationCache = new AutomationCache(new InMemoryAutomationStore(), Log.Logger);

		var services = new ServiceCollection();
		services.AddScoped<IProfileService>(_ => profileService);
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		return new CachingInitializeBackgroundService(new StartedHostLifetime(),
			profileCache,
			folderCache,
			scriptCache,
			automationCache,
			scopeFactory,
			readiness,
			Log.Logger);
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = new(true);
		public CancellationToken ApplicationStopped { get; } = new(true);

		public void StopApplication()
		{
		}
	}

	private sealed class RecordingProfileService : IProfileService
	{
		public List<string> CreatedNames { get; } = [];

		public Task<Result<ProfileEntity, ProfileError>> Create(
			string name,
			ProfileLayoutType? layoutType = null,
			int? defaultRows = null,
			int? defaultColumns = null,
			string? defaultBackgroundColor = null,
			int? defaultWidgetSpacing = null,
			int? defaultWidgetBorderRadius = null)
		{
			CreatedNames.Add(name);
			var profile = new ProfileEntity { Id = Guid.NewGuid(), Name = name };
			return Task.FromResult(Result.Ok<ProfileEntity, ProfileError>(profile));
		}

		public Task<Result<ProfileEntity, ProfileError>> Update(
			Guid id,
			string? name,
			int? order,
			int? defaultRows,
			int? defaultColumns,
			string? defaultBackgroundColor,
			int? defaultWidgetSpacing,
			int? defaultWidgetBorderRadius)
			=> throw new NotSupportedException();

		public Task<Result<ProfileError>> Delete(Guid id) => throw new NotSupportedException();
	}
}
