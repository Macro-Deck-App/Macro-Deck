using MacroDeck.Plugin.Protocol.Callbacks;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

/// <summary>
/// Pins <see cref="HostApis" /> and <see cref="HostOperations" /> against hard-coded literals, the
/// same pinning treatment <see cref="Capabilities.CapabilityOperationStabilityTests" /> gives the
/// capability-direction vocabulary.
/// </summary>
[TestFixture]
public class HostApiStabilityTests
{
	private static readonly string[] _expectedApisSortedOrdinal =
	[
		"action-interactions",
		"config",
		"deck",
		"devices",
		"folder-views",
		"layouts",
		"notifications",
		"scripts",
		"ui",
		"user-variables",
		"variable-values",
		"variables",
		"widget-types",
		"widgets",
	];

	private static readonly IReadOnlyDictionary<string, string[]> _expectedOperations
		= new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			[HostApis.Variables] = ["list", "get", "create", "set", "delete"],
			[HostApis.UserVariables] = ["apply", "create"],
			[HostApis.Config] = ["entries", "get-string", "get-secret", "set-string", "set-secret"],
			[HostApis.Deck] = ["change-folder", "change-profile", "parent", "back"],
			[HostApis.Scripts] = ["run"],
			[HostApis.Widgets] = ["apply", "invalidate-icon"],
			[HostApis.Notifications] = ["notify", "dismiss"],
			[HostApis.ActionInteractions] = ["request-item-picker", "request-device-picker", "show-modal"],
			[HostApis.Ui] = ["snapshot", "patch", "fault"],
			[HostApis.Devices] =
				["register", "update", "presence", "unregister", "interaction", "icon", "widget-icon", "close"],
			[HostApis.VariableValues] = ["value", "invalidate"],
			[HostApis.Layouts] = ["register", "unregister"],
			[HostApis.FolderViews] = ["register", "unregister"],
			[HostApis.WidgetTypes] = ["register", "unregister"],
		};

	[Test]
	public void The_host_api_set_is_exactly_the_frozen_literal()
	{
		var actualSorted = HostApis.All.OrderBy(api => api, StringComparer.Ordinal).ToArray();

		Assert.That(actualSorted, Is.EqualTo(_expectedApisSortedOrdinal));
	}

	[Test]
	public void Events_is_not_a_host_api_because_event_publish_already_covers_it()
		=> Assert.That(HostApis.IsKnown("events"), Is.False);

	[Test]
	public void Every_host_api_has_a_frozen_operation_list()
	{
		Assert.Multiple(() =>
		{
			foreach (var (api, operations) in _expectedOperations)
			{
				Assert.That(HostOperations.For(api), Is.EqualTo(operations), $"operations for '{api}'");
			}
		});
	}

	[Test]
	public void Operations_are_defined_for_exactly_the_known_apis()
	{
		Assert.That(_expectedOperations.Keys, Is.EquivalentTo(HostApis.All));
	}

	[TestCaseSource(nameof(_apisAndOperations))]
	public void Is_known_is_true_for_every_documented_pair(string api, string operation)
		=> Assert.That(HostOperations.IsKnown(api, operation), Is.True);

	[Test]
	public void Is_known_is_false_for_an_unknown_operation()
		=> Assert.That(HostOperations.IsKnown(HostApis.Variables, "not-a-real-operation"), Is.False);

	[Test]
	public void Is_known_is_false_for_an_unknown_api()
		=> Assert.That(HostOperations.IsKnown("not-a-real-api", "list"), Is.False);

	private static IEnumerable<TestCaseData> _apisAndOperations()
		=> _expectedOperations.SelectMany(pair =>
			pair.Value.Select(operation => new TestCaseData(pair.Key, operation)));
}
