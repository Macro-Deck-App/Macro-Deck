using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Infrastructure.Plugins;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class DotnetMuxerLocatorTests
{
	[Test]
	public void A_prerelease_runtime_is_read_by_its_numeric_core()
	{
		const string output =
			"Microsoft.NETCore.App 10.0.0-rc.1.24001.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]";

		Assert.That(DotnetMuxerLocator.ParseInstalledFrameworks(output)[NetCore],
			Is.EqualTo(new[] { new Version(10, 0, 0) }));
	}

	[Test]
	public void Malformed_and_empty_output_yields_no_frameworks_rather_than_throwing()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DotnetMuxerLocator.ParseInstalledFrameworks(string.Empty), Is.Empty);
			Assert.That(DotnetMuxerLocator.ParseInstalledFrameworks("not a runtime line"), Is.Empty);
			Assert.That(DotnetMuxerLocator.ParseInstalledFrameworks("Microsoft.NETCore.App nonsense"), Is.Empty);
		});
	}

	[Test]
	public void Duplicate_runtime_versions_are_collapsed()
	{
		const string output = """
							  Microsoft.NETCore.App 10.0.0 [/a]
							  Microsoft.NETCore.App 10.0.0 [/b]
							  """;

		Assert.That(DotnetMuxerLocator.ParseInstalledFrameworks(output)[NetCore], Has.Count.EqualTo(1));
	}

	[Test]
	public void By_default_a_plugin_runs_on_its_own_major_at_its_minor_or_later()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Requires(NetCore, "10.0").IsSatisfiedBy([new Version(10, 0, 3)]), Is.True);
			Assert.That(Requires(NetCore, "10.0").IsSatisfiedBy([new Version(10, 2, 0)]), Is.True);
			Assert.That(Requires(NetCore, "10.0").IsSatisfiedBy([new Version(11, 0, 0)]), Is.False);
			Assert.That(Requires(NetCore, "10.0").IsSatisfiedBy([new Version(8, 0, 0)]), Is.False);
			Assert.That(Requires(NetCore, "10.2").IsSatisfiedBy([new Version(10, 1, 0)]), Is.False);
			Assert.That(Requires(NetCore, "10.0").IsSatisfiedBy([]), Is.False);
		});
	}

	private const string AspNetCore = "Microsoft.AspNetCore.App";
	private const string NetCore = DotnetFrameworkRequirement.NetCoreAppFramework;
	private const string WindowsDesktop = "Microsoft.WindowsDesktop.App";

	private string _root = null!;

	private static string MuxerName => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

	[SetUp]
	public void CreateRoot()
	{
		_root = Path.Combine(Path.GetTempPath(), "md-muxer-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void DeleteRoot() => Directory.Delete(_root, recursive: true);

	private string BundledRoot(params (string Framework, string Version)[] frameworks)
	{
		var root = Path.Combine(_root, "host", "runtime");
		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Combine(root, MuxerName), string.Empty);
		foreach (var (framework, version) in frameworks)
		{
			Directory.CreateDirectory(Path.Combine(root, "shared", framework, version));
		}

		return root;
	}

	private string SystemMuxer()
	{
		var directory = Path.Combine(_root, "system");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, MuxerName);
		File.WriteAllText(path, string.Empty);
		return path;
	}

	private static Dictionary<string, IReadOnlyList<Version>> Frameworks(params (string Framework, string Version)[] frameworks)
		=> frameworks.GroupBy(f => f.Framework)
			.ToDictionary(g => g.Key, g => (IReadOnlyList<Version>)g.Select(f => Version.Parse(f.Version)).ToList());

	private static DotnetMuxerLocator Locator(string bundledRoot,
		string? systemMuxer,
		IReadOnlyDictionary<string, IReadOnlyList<Version>>? systemFrameworks,
		Action? onProbe = null)
		=> new(Serilog.Core.Logger.None,
			bundledRoot,
			() => systemMuxer is null ? [] : [systemMuxer],
			_ =>
			{
				onProbe?.Invoke();
				return systemFrameworks;
			});

	private static DotnetFrameworkRequirement Requires(string name,
		string version,
		DotnetRollForward rollForward = DotnetRollForward.Minor)
		=> new() { Name = name, Version = Version.Parse(version), RollForward = rollForward };

	[Test]
	public void The_bundled_runtime_is_chosen_over_a_system_install_when_it_has_every_framework()
	{
		var bundled = BundledRoot((NetCore, "10.0.12"), (AspNetCore, "10.0.12"));
		var locator = Locator(bundled, SystemMuxer(), Frameworks((NetCore, "10.0.5"), (AspNetCore, "10.0.5")));

		var selection = locator.Locate([Requires(NetCore, "10.0.0"), Requires(AspNetCore, "10.0.0")]);

		Assert.That(selection?.Muxer.ExecutablePath, Is.EqualTo(Path.Combine(bundled, MuxerName)));
		Assert.That(selection?.UnmetRequirement, Is.Null);
	}

	[Test]
	public void A_system_install_is_not_probed_while_the_bundled_runtime_satisfies_the_plugin()
	{
		var probes = 0;
		var locator = Locator(BundledRoot((NetCore, "10.0.12")),
			SystemMuxer(),
			Frameworks((NetCore, "10.0.12")),
			() => probes++);

		locator.Locate([Requires(NetCore, "10.0.0")]);

		Assert.That(probes, Is.Zero);
	}

	[Test]
	public void A_framework_the_bundled_runtime_lacks_is_served_by_the_system_install()
	{
		var system = SystemMuxer();
		var locator = Locator(BundledRoot((NetCore, "10.0.12"), (AspNetCore, "10.0.12")),
			system,
			Frameworks((NetCore, "10.0.3"), (WindowsDesktop, "10.0.3")));

		var selection = locator.Locate([Requires(NetCore, "10.0.0"), Requires(WindowsDesktop, "10.0.0")]);

		Assert.That(selection?.Muxer.ExecutablePath, Is.EqualTo(system));
		Assert.That(selection?.UnmetRequirement, Is.Null);
	}

	[Test]
	public void A_system_install_whose_frameworks_cannot_be_read_is_still_launched()
	{
		var system = SystemMuxer();
		var locator = Locator(BundledRoot((NetCore, "10.0.12")), system, systemFrameworks: null);

		var selection = locator.Locate([Requires(NetCore, "9.0.0")]);

		Assert.That(selection?.Muxer.ExecutablePath, Is.EqualTo(system));
		Assert.That(selection?.UnmetRequirement, Is.Null);
	}

	[Test]
	public void When_nothing_satisfies_the_plugin_the_missing_framework_is_reported()
	{
		var bundled = BundledRoot((NetCore, "10.0.12"));
		var locator = Locator(bundled, SystemMuxer(), Frameworks((NetCore, "8.0.11")));

		var selection = locator.Locate([Requires(NetCore, "9.0.0")]);

		Assert.That(selection?.Muxer.ExecutablePath, Is.EqualTo(Path.Combine(bundled, MuxerName)));
		Assert.That(selection?.UnmetRequirement?.Name, Is.EqualTo(NetCore));
	}

	[Test]
	public void No_runtime_anywhere_yields_no_selection()
	{
		var locator = Locator(Path.Combine(_root, "missing"), systemMuxer: null, systemFrameworks: null);

		Assert.That(locator.Locate([Requires(NetCore, "10.0.0")]), Is.Null);
	}

	[Test]
	public void A_plugin_that_rolls_forward_across_majors_runs_on_the_bundled_runtime()
	{
		var bundled = BundledRoot((NetCore, "10.0.12"));
		var locator = Locator(bundled, systemMuxer: null, systemFrameworks: null);

		Assert.Multiple(() =>
		{
			Assert.That(locator.Locate([Requires(NetCore, "9.0.0", DotnetRollForward.Major)])?.UnmetRequirement,
				Is.Null);
			Assert.That(locator.Locate([Requires(NetCore, "9.0.0")])?.UnmetRequirement?.Name, Is.EqualTo(NetCore));
		});
	}

	[Test]
	public void A_prerelease_bundled_runtime_is_read_by_its_numeric_core()
	{
		var locator = Locator(BundledRoot((NetCore, "10.0.0-rc.2.25502.107")), systemMuxer: null, systemFrameworks: null);

		Assert.That(locator.Locate([Requires(NetCore, "10.0.0")])?.UnmetRequirement, Is.Null);
	}

	[Test]
	public void Roll_forward_policies_decide_which_installed_versions_count()
	{
		Version[] installed = [new(10, 0, 12)];

		Assert.Multiple(() =>
		{
			Assert.That(Requires(NetCore, "10.0.3", DotnetRollForward.Disable).IsSatisfiedBy(installed), Is.False);
			Assert.That(Requires(NetCore, "10.0.12", DotnetRollForward.Disable).IsSatisfiedBy(installed), Is.True);
			Assert.That(Requires(NetCore, "10.0.3", DotnetRollForward.LatestPatch).IsSatisfiedBy(installed), Is.True);
			Assert.That(Requires(NetCore, "9.0.0", DotnetRollForward.LatestMinor).IsSatisfiedBy(installed), Is.False);
			Assert.That(Requires(NetCore, "9.0.0", DotnetRollForward.LatestMajor).IsSatisfiedBy(installed), Is.True);
			Assert.That(Requires(NetCore, "11.0.0", DotnetRollForward.Major).IsSatisfiedBy(installed), Is.False);
		});
	}

	[Test]
	public void Every_framework_listed_by_the_muxer_is_parsed()
	{
		const string output = """
							  Microsoft.AspNetCore.App 10.0.3 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]
							  Microsoft.NETCore.App 10.0.3 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
							  Microsoft.WindowsDesktop.App 10.0.3 [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]
							  """;

		var frameworks = DotnetMuxerLocator.ParseInstalledFrameworks(output);

		Assert.That(frameworks.Keys, Is.EquivalentTo(new[] { AspNetCore, NetCore, WindowsDesktop }));
	}
}
