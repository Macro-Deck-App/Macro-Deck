using System.Text.Json;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Components;
using MacroDeckHost.Tests.UnitTests.MusicPlayer;
using MacroDeckHost.Widgets.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The device picker as a dialog. What matters to a caller is that a row answers with the device's own
/// id - that is what the host reads back and transfers playback to - and that the three ways a list can
/// come back empty stay distinguishable, because "no devices" and "could not load" are different things
/// to show a user.
/// </summary>
[TestFixture]
public class MusicPlayerDevicePickerTests
{
	private const string _rowPrefix = "picker.results.";

	private static UiSurface Surface(string viewId = MusicPlayerDevicePickerUiProvider.PickViewId,
		string? instanceId = "inst-1")
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiDialogSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(viewId),
		};

		if (instanceId is not null)
		{
			attributes[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(
				new Dictionary<string, string>(StringComparer.Ordinal) { ["instanceId"] = instanceId });
		}

		return new UiSurface
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = attributes,
		};
	}

	private static MusicPlayerDevicePickerUiProvider Provider(IMusicPlayer? player)
		=> new(new SinglePlayerRegistry(player), new LoggerConfiguration().CreateLogger());

	private static async Task<UiNode> OpenAsync(IMusicPlayer? player)
	{
		var session = await Provider(player).CreateSessionAsync(
			new UiSessionRequest { Surface = Surface(), UiModelVersion = 1 },
			CancellationToken.None);

		Assert.That(session, Is.Not.Null);

		await using (session)
		{
			var settled = new TaskCompletionSource();
			session!.Changed += (_, _) => settled.TrySetResult();

			// The load is started by the constructor and lands on its own thread; the first tree may or
			// may not already carry it.
			await Task.WhenAny(settled.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

			return session.BuildTree().Root;
		}
	}

	/// <summary>
	/// The rows, which are the direct children of the results list that answer. The list's other children
	/// are the three message branches, which carry no answer because pressing one means nothing.
	/// </summary>
	private static List<UiNode> Rows(UiNode root)
	{
		var rows = new List<UiNode>();

		Walk(root, rows);

		return rows;

		static void Walk(UiNode node, List<UiNode> into)
		{
			if (node.Id.StartsWith(_rowPrefix, StringComparison.Ordinal) &&
				node.Id.LastIndexOf('.') == _rowPrefix.Length - 1 &&
				node.Properties.ContainsKey(UiComponentProperties.Answer))
			{
				into.Add(node);
			}

			foreach (var child in node.Children)
			{
				Walk(child, into);
			}
		}
	}

	/// <summary>
	/// Whether a node with this id is in the tree. Asserted rather than the text it shows: a
	/// <c>LocalizedText</c> crosses the wire as a reference each reader resolves in its own language, so
	/// there is no English string in the tree to look for.
	/// </summary>
	private static bool HasNode(UiNode node, string id)
		=> string.Equals(node.Id, id, StringComparison.Ordinal) || node.Children.Any(child => HasNode(child, id));

	/// <summary>Whether any literal string in the subtree contains this text. Only for the values the
	/// player itself supplies - a device's name, its type, its volume - which are not localized.</summary>
	private static bool Mentions(UiNode node, string text)
	{
		foreach (var property in node.Properties)
		{
			if (property.Value.ValueKind == JsonValueKind.String &&
				property.Value.GetString()?.Contains(text, StringComparison.Ordinal) == true)
			{
				return true;
			}
		}

		return node.Children.Any(child => Mentions(child, text));
	}

	[Test]
	public async Task A_dialog_for_another_view_is_declined()
	{
		var session = await Provider(new FakeDevicePlayer())
			.CreateSessionAsync(new UiSessionRequest
					{ Surface = Surface(viewId: "weather-details"), UiModelVersion = 1 },
				CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task A_dialog_naming_no_instance_is_declined()
	{
		var session = await Provider(new FakeDevicePlayer())
			.CreateSessionAsync(new UiSessionRequest { Surface = Surface(instanceId: null), UiModelVersion = 1 },
				CancellationToken.None);

		// Which player to list is the one thing the dialog cannot work out for itself.
		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task Every_row_answers_with_its_own_device_id()
	{
		var player = new FakeDevicePlayer
		{
			Devices =
			[
				new MusicPlayerDevice("d1", "Living Room"),
				new MusicPlayerDevice("d2", "Kitchen"),
			],
		};

		var rows = Rows(await OpenAsync(player));

		Assert.That(rows, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			// The answer is the id the host issued into the dialog, which is what makes the value coming
			// back one it can look up rather than one it has to trust.
			Assert.That(rows[0].Properties[UiComponentProperties.Answer].GetString(), Is.EqualTo("d1"));
			Assert.That(rows[1].Properties[UiComponentProperties.Answer].GetString(), Is.EqualTo("d2"));
		});
	}

	[Test]
	public async Task A_row_shows_the_device_name_and_its_type()
	{
		var player = new FakeDevicePlayer
		{
			Devices = [new MusicPlayerDevice("d1", "Living Room", Type: "Speaker")],
		};

		var row = Rows(await OpenAsync(player)).Single();

		Assert.Multiple(() =>
		{
			Assert.That(Mentions(row, "Living Room"), Is.True);
			Assert.That(Mentions(row, "Speaker"), Is.True);
		});
	}

	[Test]
	public async Task The_active_device_is_marked_and_the_others_are_not()
	{
		var player = new FakeDevicePlayer
		{
			Devices =
			[
				new MusicPlayerDevice("d1", "Living Room", IsActive: true),
				new MusicPlayerDevice("d2", "Kitchen"),
			],
		};

		var rows = Rows(await OpenAsync(player));

		Assert.Multiple(() =>
		{
			Assert.That(HasNode(rows[0], _rowPrefix + "d1.active"), Is.True);
			Assert.That(HasNode(rows[1], _rowPrefix + "d2.active"), Is.False);
		});
	}

	/// <summary>
	/// A device reporting no volume and one reporting zero are different facts. Anything that treats the
	/// volume as merely truthy hides a muted device's volume, which is the one worth seeing.
	/// </summary>
	[Test]
	public async Task A_volume_of_zero_is_shown_and_an_absent_volume_is_not()
	{
		var player = new FakeDevicePlayer
		{
			Devices =
			[
				new MusicPlayerDevice("muted", "Muted", VolumePercent: 0),
				new MusicPlayerDevice("unknown", "Unknown"),
			],
		};

		var rows = Rows(await OpenAsync(player));

		Assert.Multiple(() =>
		{
			Assert.That(Mentions(rows[0], "0%"), Is.True);
			Assert.That(Mentions(rows[1], "%"), Is.False);
		});
	}

	[Test]
	public async Task A_player_that_cannot_switch_devices_says_so_rather_than_showing_an_empty_list()
	{
		var root = await OpenAsync(new FakeMusicPlayer());

		Assert.Multiple(() =>
		{
			Assert.That(Rows(root), Is.Empty);
			Assert.That(HasNode(root, _rowPrefix + "unsupported"), Is.True);
		});
	}

	/// <summary>
	/// The contract says a player reports a failed read by throwing and an empty library by answering
	/// with an empty list. Collapsing the two is what makes an unreachable player render as "no devices".
	/// </summary>
	[Test]
	public async Task A_failed_read_is_distinguishable_from_a_player_with_no_devices()
	{
		var failed = await OpenAsync(new FakeDevicePlayer
		{
			ListFailure = new InvalidOperationException("unreachable"),
		});

		var empty = await OpenAsync(new FakeDevicePlayer());

		Assert.Multiple(() =>
		{
			Assert.That(HasNode(failed, _rowPrefix + "failed"), Is.True);
			Assert.That(HasNode(failed, _rowPrefix + "empty"), Is.False);

			Assert.That(HasNode(empty, _rowPrefix + "empty"), Is.True);
			Assert.That(HasNode(empty, _rowPrefix + "failed"), Is.False);
		});
	}
}
