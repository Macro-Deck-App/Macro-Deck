using System.Text.Json;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

/// <summary>
/// Regression coverage for event dispatch: the round trip the whole framework exists for, and the three ways it
/// can be got wrong in a way nothing else notices - a dispatch that updates the tree but not the state, a
/// dispatch that throws on something a newer renderer legitimately sends, and a write that goes through a
/// binding declared read-only. Each test traces to one of acceptance scenarios 26-29 for issue #540.
///
/// <para>
/// The events are dispatched through <see cref="UiView.Dispatch" /> with a <see cref="UiEvent" /> built here.
/// The test host's <c>ById(...).Change(...)</c> sugar is downstream of this package and cannot be referenced
/// from it; what it will do is exactly this call.
/// </para>
/// </summary>
[TestFixture]
public class UiEventDispatchTests
{
	private static readonly string[] _healthyNodeIds = ["healthy"];

	/// <summary>Why the declining handler declines. The test owns it, so the reason reaching
	/// <see cref="UiDispatchResult.Reason" /> is checked against what the handler said rather than against
	/// whatever the runtime composed around it.</summary>
	private const string _declineReason = "An API key is required.";

	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static UiDispatchResult Change(UiView view, string nodeId, string value)
		=> view.Dispatch(new UiEvent
		{
			NodeId = nodeId,
			Name = UiConfigEvents.Change,
			Data = UiCanonicalJson.ToElement(value),
		});

	private static UiDispatchResult Submit(UiView view, string nodeId)
		=> view.Dispatch(new UiEvent { NodeId = nodeId, Name = UiConfigEvents.Submit });

	private static UiNode? FindById(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (FindById(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	/// <summary>The SampleFlow fixture's credentials step, reduced to what a dispatch needs: a bound text field,
	/// a bound choice, and the step itself as a node that is not an input.</summary>
	private static (UiView View, UiState<string> ApiKeyState) BuildFlow()
	{
		var apiKeyState = new UiState<string>("initial");
		var modeState = new UiState<string>("simple");

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStep
					{
						Key = "credentials",
						Children =
						[
							new UiStringInput { Key = "apiKey", Binding = Bind.To(apiKeyState) },
							new UiChoiceInput { Key = "mode", Binding = Bind.To(modeState) },
						],
					},
				],
			});

		return (view, apiKeyState);
	}

	[Test]
	public void A_change_event_writes_through_the_binding_and_the_resulting_patch_carries_the_new_value()
	{
		var (view, apiKeyState) = BuildFlow();
		view.DrainPatches();

		var result = Change(view, "apiKey", "k-1");
		var patches = view.DrainPatches();

		Assert.That(result.IsAccepted, Is.True, result.Reason);
		Assert.That(patches, Has.Count.EqualTo(1));
		Assert.That(patches[0].Operations, Has.Count.EqualTo(1));

		var operation = patches[0].Operations[0];

		Assert.Multiple(() =>
		{
			// The state, not just the tree: a dispatch that updated one without the other leaves the plugin
			// reading stale data when it submits.
			Assert.That(apiKeyState.Peek(), Is.EqualTo("k-1"));
			Assert.That(operation.Op, Is.EqualTo(UiPatchOperations.SetProperties));
			Assert.That(operation.NodeId, Is.EqualTo("apiKey"));
			Assert.That(operation.Properties![UiConfigProperties.Value].GetString(), Is.EqualTo("k-1"));
			Assert.That(FindById(view.Tree.Root, "apiKey")!.Properties[UiConfigProperties.Value].GetString(),
				Is.EqualTo("k-1"));
		});
	}

	[Test]
	public void An_unknown_event_name_and_an_event_for_an_unknown_node_are_ignored_without_throwing()
	{
		var (view, apiKeyState) = BuildFlow();
		view.DrainPatches();

		var revisionBefore = view.Revision;

		var unknownName = view.Dispatch(new UiEvent { NodeId = "apiKey", Name = "teleport" });
		var unknownNode = view.Dispatch(new UiEvent { NodeId = "no-such-node", Name = UiConfigEvents.Change });
		var notAnInput = view.Dispatch(new UiEvent
		{
			NodeId = "setup.credentials",
			Name = UiConfigEvents.Change,
		});

		Assert.Multiple(() =>
		{
			Assert.That(unknownName.Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
			Assert.That(unknownNode.Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
			Assert.That(notAnInput.Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
			Assert.That(unknownName.Reason, Is.Not.Null);
			Assert.That(unknownNode.Reason, Is.Not.Null);
			Assert.That(notAnInput.Reason, Is.Not.Null);
			Assert.That(view.Revision, Is.EqualTo(revisionBefore));
			Assert.That(view.DrainPatches(), Is.Empty);
		});

		// The session survives: an unknown name is the shape of a newer renderer talking to an older plugin, and
		// it must not poison what comes next.
		Assert.That(Change(view, "apiKey", "k-1").IsAccepted, Is.True);
		Assert.That(apiKeyState.Peek(), Is.EqualTo("k-1"));

		// The positive half: every frozen event name, dispatched to a node that declares it, is accepted.
		var seen = new List<string>();
		var handlers = UiConfigEvents.WellKnown
			.Select(name => UiEventHandler.On(name, () => seen.Add(name)))
			.ToList();

		var everyEvent = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children = [new UiStringInput { Key = "field", Events = handlers }],
			});

		Assert.Multiple(() =>
		{
			foreach (var name in UiConfigEvents.WellKnown)
			{
				var result = everyEvent.Dispatch(new UiEvent { NodeId = "field", Name = name });

				Assert.That(result.IsAccepted, Is.True, $"'{name}' was not accepted: {result.Reason}");
			}

			Assert.That(seen, Is.EqualTo(UiConfigEvents.WellKnown).AsCollection);
		});
	}

	[Test]
	public void A_change_event_against_a_read_only_binding_is_rejected_and_does_not_mutate_state()
	{
		var backing = new UiState<string>("original");

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "readOnly", Binding = Bind.ReadOnly(UiValue.From(() => backing.Value)) },
				],
			});

		view.DrainPatches();
		var revisionBefore = view.Revision;

		var result = Change(view, "readOnly", "hacked");

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(result.Reason, Is.Not.Null.And.Not.Empty);
			Assert.That(backing.Peek(), Is.EqualTo("original"));
			Assert.That(view.DrainPatches(), Is.Empty);
			Assert.That(view.Revision, Is.EqualTo(revisionBefore));
			Assert.That(FindById(view.Tree.Root, "readOnly")!.Properties[UiConfigProperties.Value].GetString(),
				Is.EqualTo("original"));
		});
	}

	[Test]
	public void A_faulting_event_handler_surfaces_on_HandlerFaulted_and_does_not_escape_Dispatch()
	{
		var healthyState = new UiState<string>("ok");

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput
					{
						Key = "faulting",
						Binding = Bind.Custom<string>(() => "stored",
							_ => throw new InvalidOperationException("the handler broke")),
					},
					new UiStringInput { Key = "healthy", Binding = Bind.To(healthyState) },
				],
			});

		view.DrainPatches();

		var faults = new List<UiHandlerFaultEventArgs>();
		view.HandlerFaulted += (_, args) => faults.Add(args);

		var result = default(UiDispatchResult);

		Assert.DoesNotThrow(() => result = Change(view, "faulting", "boom"),
			"a handler's fault must not escape into whoever dispatched the event");

		var faultPatches = view.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(result.Reason, Is.Not.Null.And.Not.Empty);

			Assert.That(faults, Has.Count.EqualTo(1));
			Assert.That(faults[0].NodeId, Is.EqualTo("faulting"));
			Assert.That(faults[0].EventName, Is.EqualTo(UiConfigEvents.Change));
			Assert.That(faults[0].Exception, Is.TypeOf<InvalidOperationException>());

			// Nothing changed, so no patch - a zero-operation patch would be inapplicable outright.
			Assert.That(faultPatches, Is.Empty);
		});

		// One bad handler must not hang the dialog: the next healthy input still works.
		var healthy = Change(view, "healthy", "next");
		var healthyPatches = view.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(healthy.IsAccepted, Is.True, healthy.Reason);
			Assert.That(healthyState.Peek(), Is.EqualTo("next"));
			Assert.That(healthyPatches, Has.Count.EqualTo(1));
			Assert.That(healthyPatches[0].Operations.Select(operation => operation.NodeId),
				Is.EqualTo(_healthyNodeIds).AsCollection);
		});
	}

	[Test]
	public void A_handler_that_declines_an_event_is_rejected_without_being_reported_as_a_fault()
	{
		// The other direction of the test above, and the distinction a host acts on: "the plugin declined this
		// input" is an answer a renderer shows the user, while "the plugin's code is broken" is something a host
		// logs as an error. If declining raised HandlerFaulted, every failed validation would look like a bug.
		var apiKey = new UiState<string>(string.Empty);
		var submitAttempted = new UiState<bool>(false);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStep
					{
						Key = "credentials",
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Submit,
								_ =>
								{
									if (apiKey.Peek().Length > 0)
									{
										return UiEventOutcome.Accepted;
									}

									// Written before declining, so the refusal has to carry it out with it.
									submitAttempted.Value = true;

									return UiEventOutcome.Rejected(_declineReason);
								}),
						],
						Children =
						[
							new UiStringInput { Key = "apiKey", Binding = Bind.To(apiKey) },
							new UiWhen
							{
								Key = "messageGate",
								Condition = () => submitAttempted.Value,
								Content = () => new UiValidationMessage
								{
									Key = "apiKeyMessage",
									Text = _declineReason,
									For = "apiKey",
								},
							},
						],
					},
				],
			});

		view.DrainPatches();

		var faults = new List<UiHandlerFaultEventArgs>();
		view.HandlerFaulted += (_, args) => faults.Add(args);

		var declined = Submit(view, "setup.credentials");
		var declinePatches = view.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(declined.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(declined.Reason,
				Is.EqualTo(_declineReason),
				"the handler's own reason is what the client is told, verbatim");
			Assert.That(faults, Is.Empty, "declining an event is not a defect and must not be reported as one");

			// The state the handler wrote before declining still produced its patch, so the user gets the message
			// that explains the refusal rather than a refusal with nothing shown.
			Assert.That(declinePatches, Has.Count.EqualTo(1));
			Assert.That(declinePatches[0].Operations.Select(operation => operation.Op),
				Has.One.EqualTo(UiPatchOperations.InsertNode));
			Assert.That(FindById(view.Tree.Root, "setup.credentials.apiKeyMessage"), Is.Not.Null);
		});

		// And the same handler accepts once the reason to decline is gone, which is what makes it a decision rather
		// than a permanent refusal.
		Assert.That(Change(view, "apiKey", "k-1").IsAccepted, Is.True);

		view.DrainPatches();

		var accepted = Submit(view, "setup.credentials");

		Assert.Multiple(() =>
		{
			Assert.That(accepted.IsAccepted, Is.True, accepted.Reason);
			Assert.That(faults, Is.Empty);
		});
	}

	[Test]
	public void A_change_event_whose_payload_is_the_wrong_json_kind_is_rejected_rather_than_throwing()
	{
		var count = new UiState<double>(1);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children = [new UiNumberInput { Key = "count", Binding = Bind.To(count) }],
			});

		view.DrainPatches();

		var wrongKind = view.Dispatch(new UiEvent
		{
			NodeId = "count",
			Name = UiConfigEvents.Change,
			Data = UiCanonicalJson.ToElement("not-a-number"),
		});

		var absent = view.Dispatch(new UiEvent { NodeId = "count", Name = UiConfigEvents.Change });

		Assert.Multiple(() =>
		{
			Assert.That(wrongKind.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(absent.Outcome, Is.EqualTo(UiDispatchOutcome.Rejected));
			Assert.That(count.Peek(), Is.EqualTo(1));
			Assert.That(view.DrainPatches(), Is.Empty);
		});

		var accepted = view.Dispatch(new UiEvent
		{
			NodeId = "count",
			Name = UiConfigEvents.Change,
			Data = JsonSerializer.SerializeToElement(7),
		});

		Assert.That(accepted.IsAccepted, Is.True, accepted.Reason);
		Assert.That(count.Peek(), Is.EqualTo(7));
	}
}
