using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Tests.UnitTests.Adb;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginAdbAccessPolicyTests
{
	private const string PluginId = "com.example.android";

	[Test]
	public async Task Adb_switched_off_means_not_enabled_even_when_plugins_are_not_allowed_either()
	{
		var world = new World { AdbEnabled = false, AllowPlugins = false };

		Assert.That(await world.Policy.EvaluateAsync(PluginId), Is.EqualTo(PluginAdbAccess.NotEnabled));
	}

	[Test]
	public async Task Plugin_access_switched_off_means_not_allowed_even_for_a_plugin_that_declares_adb()
	{
		var world = new World { AllowPlugins = false, InstalledPermissions = [PluginPermissions.HostAdb] };

		Assert.That(await world.Policy.EvaluateAsync(PluginId), Is.EqualTo(PluginAdbAccess.NotAllowed));
	}

	[Test]
	public async Task An_installed_plugin_that_does_not_declare_adb_is_not_allowed()
	{
		var world = new World { InstalledPermissions = [PluginPermissions.HostVariables] };
		await world.ConnectAsync(PluginSessionOrigin.Managed);

		Assert.That(await world.Policy.EvaluateAsync(PluginId), Is.EqualTo(PluginAdbAccess.NotAllowed));
	}

	[Test]
	public async Task An_installed_plugin_that_declares_adb_is_allowed()
	{
		var world = new World { InstalledPermissions = [PluginPermissions.HostAdb] };
		await world.ConnectAsync(PluginSessionOrigin.Managed);

		Assert.That(await world.Policy.EvaluateAsync(PluginId), Is.EqualTo(PluginAdbAccess.Available));
	}

	[Test]
	public async Task A_self_registered_session_is_allowed_without_a_declaration()
	{
		var world = new World { InstalledPermissions = null };
		await world.ConnectAsync(PluginSessionOrigin.SelfRegistered);

		Assert.That(await world.Policy.EvaluateAsync(PluginId), Is.EqualTo(PluginAdbAccess.Available));
	}

	[Test]
	public async Task A_refreshed_setting_applies_without_reading_the_preferences_again()
	{
		var world = new World { InstalledPermissions = [PluginPermissions.HostAdb] };
		await world.ConnectAsync(PluginSessionOrigin.Managed);
		var before = await world.Policy.EvaluateAsync(PluginId);

		world.Policy.Refresh(new AdbSettings(true, null, true, null, AllowPlugins: false));
		var after = await world.Policy.EvaluateAsync(PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.EqualTo(PluginAdbAccess.Available));
			Assert.That(after, Is.EqualTo(PluginAdbAccess.NotAllowed));
		});
	}

	private sealed class World
	{
		private readonly Lazy<PluginAdbAccessPolicy> _policy;

		public World() => _policy = new Lazy<PluginAdbAccessPolicy>(Build);

		public bool AdbEnabled { get; init; } = true;

		public bool AllowPlugins { get; init; } = true;

		public IReadOnlyList<string>? InstalledPermissions { get; init; } = [];

		public PluginSessionRegistry Sessions { get; } = new(TimeProvider.System, Serilog.Core.Logger.None);

		public PluginAdbAccessPolicy Policy => _policy.Value;

		public async Task ConnectAsync(PluginSessionOrigin origin)
		{
			await Sessions.Create(new PluginSessionRecord
			{
				SessionId = "session-1",
				PluginId = PluginId,
				DisplayName = "Android",
				Origin = origin,
				NegotiatedVersion = 1,
				Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
				DeclaredCapabilities = [],
				State = PluginSessionState.Awaiting,
				CreatedAt = TimeProvider.System.GetUtcNow()
			});
			Sessions.TryAttach("session-1", new FakePluginConnection(), null);
		}

		private PluginAdbAccessPolicy Build()
		{
			var adb = new FakeAdbManager { Status = AdbStatus.Disabled with { Enabled = AdbEnabled } };
			var preferences = new FakeAdbPreferenceService
			{
				AdbSettings = new AdbSettings(AdbEnabled, null, true, null, AllowPlugins: AllowPlugins)
			};
			var services = new ServiceCollection();
			services.AddScoped<IAppPreferenceService>(_ => preferences);

			return new PluginAdbAccessPolicy(adb,
				Sessions,
				new SingleInstalledPlugin(InstalledPermissions),
				new FixedManifestReader(InstalledPermissions),
				services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
		}
	}

	private sealed class SingleInstalledPlugin(IReadOnlyList<string>? permissions) : IPluginInstallationCatalog
	{
		public IReadOnlyList<InstalledPlugin> Discover() => [];

		public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
		{
			version = permissions is null
				? null
				: new InstalledPluginVersion { Version = "1.0.0", VersionDirectory = "v", ManifestPath = "v/manifest.json" };
			return version is not null;
		}

		public void Invalidate()
		{
		}
	}

	private sealed class FixedManifestReader(IReadOnlyList<string>? permissions) : IPluginManifestReader
	{
		public PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion)
			=> PluginManifestReadResult.Ok(new PluginManifest
			{
				ManifestVersion = 1,
				Id = expectedPluginId,
				Name = "Android",
				Version = expectedVersion,
				Entrypoints = new Dictionary<string, PluginEntrypoint>(),
				Permissions = permissions
			});

		public PluginManifestReadResult ReadFromJson(string json,
			string? versionDirectory,
			string expectedPluginId,
			string expectedVersion)
			=> throw new NotSupportedException();
	}
}
