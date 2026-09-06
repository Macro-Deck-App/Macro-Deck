using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Validation;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

/// <summary>
/// Regression coverage for the ported validation rules. The cases come from what the existing action flow
/// validation documents and does - required emptiness per type, the pattern, the length, the per-type format
/// checks, the numeric bounds, and a hidden field being skipped - not from reading this package's
/// implementation. A second validator that disagreed with the editor's would let a plugin's own preflight
/// accept a flow the editor then refuses to save, or the reverse.
/// </summary>
[TestFixture]
public class ValidationTests
{
	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static UiValidation Field(string type, object? value)
		=> new() { Type = type, Name = "field", Label = "Field", Value = value };

	[Test]
	public void A_required_field_is_empty_when_it_has_no_value_or_only_whitespace()
	{
		Assert.Multiple(() =>
		{
			Assert.That((Field(UiConfigPrimitives.String, null) with { Required = true }).Validate().IsValid,
				Is.False);
			Assert.That((Field(UiConfigPrimitives.String, "   ") with { Required = true }).Validate().Resolved(),
				Is.EqualTo("Field is required"));
			Assert.That((Field(UiConfigPrimitives.Number, double.NaN) with { Required = true }).Validate().IsValid,
				Is.False);

			// A field nobody marked required accepts nothing at all.
			Assert.That(Field(UiConfigPrimitives.String, null).Validate().IsValid, Is.True);

			// False is an answer, so a toggle is never empty - the one per-type exception the editor makes.
			Assert.That((Field(UiConfigPrimitives.Boolean, false) with { Required = true }).Validate().IsValid,
				Is.True);
			Assert.That((Field(UiConfigPrimitives.String, "a") with { Required = true }).Validate().IsValid,
				Is.True);
		});
	}

	[Test]
	public void A_value_has_to_match_the_declared_pattern_and_a_broken_pattern_never_blocks_the_field()
	{
		var mismatch = Field(UiConfigPrimitives.String, "abc") with { ValidationRegex = "^[0-9]+$" };
		var match = Field(UiConfigPrimitives.String, "123") with { ValidationRegex = "^[0-9]+$" };
		var broken = Field(UiConfigPrimitives.String, "abc") with { ValidationRegex = "([unclosed" };

		Assert.Multiple(() =>
		{
			Assert.That(mismatch.Validate().Resolved(), Is.EqualTo("Field does not match the expected format"));
			Assert.That(match.Validate().IsValid, Is.True);

			// The pattern came from the plugin, not from the user, so a plugin's mistake must not make the field
			// unfillable.
			Assert.That(broken.Validate().IsValid, Is.True);
		});
	}

	[Test]
	public void A_value_longer_than_the_declared_maximum_is_rejected()
	{
		var tooLong = Field(UiConfigPrimitives.String, "abcd") with { MaxLength = 3 };
		var exact = Field(UiConfigPrimitives.String, "abc") with { MaxLength = 3 };

		Assert.Multiple(() =>
		{
			Assert.That(tooLong.Validate().Resolved(), Is.EqualTo("Field exceeds 3 characters"));
			Assert.That(exact.Validate().IsValid, Is.True);
		});
	}

	[Test]
	public void A_url_field_rejects_a_value_the_browser_would_silently_repair()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Field(UiConfigPrimitives.Url, "https://example.com").Validate().IsValid, Is.True);
			Assert.That(Field(UiConfigPrimitives.Url, "mailto:someone@example.com").Validate().IsValid, Is.True);

			// The whole reason the editor does not just call the URL parser: a single slash parses, and then the
			// link goes nowhere.
			Assert.That(Field(UiConfigPrimitives.Url, "https:/example.com").Validate().Resolved(),
				Is.EqualTo("Field is not a valid URL"));
			Assert.That(Field(UiConfigPrimitives.Url, "example.com").Validate().IsValid, Is.False);

			// The same text under a type with no format check is accepted.
			Assert.That(Field(UiConfigPrimitives.String, "example.com").Validate().IsValid, Is.True);
		});
	}

	[Test]
	public void An_ip_address_field_accepts_a_dotted_quad_or_a_colon_bearing_hexadecimal_address()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Field(UiConfigPrimitives.IpAddress, "192.168.0.1").Validate().IsValid, Is.True);
			Assert.That(Field(UiConfigPrimitives.IpAddress, "fe80::1").Validate().IsValid, Is.True);
			Assert.That(Field(UiConfigPrimitives.IpAddress, "192.168.0.256").Validate().Resolved(),
				Is.EqualTo("Field is not a valid IP address"));
			Assert.That(Field(UiConfigPrimitives.IpAddress, "192.168.0").Validate().IsValid, Is.False);
			Assert.That(Field(UiConfigPrimitives.IpAddress, "not-an-address").Validate().IsValid, Is.False);
		});
	}

	[Test]
	public void A_json_field_rejects_a_document_that_does_not_parse()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Field(UiConfigPrimitives.Json, "{\"a\":1}").Validate().IsValid, Is.True);
			Assert.That(Field(UiConfigPrimitives.Json, "{\"a\":").Validate().Resolved(),
				Is.EqualTo("Field is not valid JSON"));
		});
	}

	[Test]
	public void The_numeric_bounds_apply_to_a_number_and_a_duration_and_to_nothing_else()
	{
		Assert.Multiple(() =>
		{
			Assert.That((Field(UiConfigPrimitives.Number, 1d) with { Min = 5 }).Validate().Resolved(),
				Is.EqualTo("Field must be at least 5"));
			Assert.That((Field(UiConfigPrimitives.Number, 9d) with { Max = 5 }).Validate().Resolved(),
				Is.EqualTo("Field must be at most 5"));
			Assert.That((Field(UiConfigPrimitives.Number, 5d) with { Min = 5, Max = 5 }).Validate().IsValid,
				Is.True);
			Assert.That((Field(UiConfigPrimitives.Duration, 10d) with { Min = 100 }).Validate().IsValid,
				Is.False);

			// A bound on anything else means nothing, which is what the editor does with it.
			Assert.That((Field(UiConfigPrimitives.Color, 1d) with { Min = 5 }).Validate().IsValid, Is.True);
		});
	}

	[Test]
	public void An_authored_rule_is_checked_after_the_declared_constraints()
	{
		var failing = Field(UiConfigPrimitives.String, "abc") with
		{
			Required = true,
			MaxLength = 2,
			Rules = [UiValidationRule.Require("Custom failure.", () => false)],
		};

		var ruleOnly = Field(UiConfigPrimitives.String, "abc") with
		{
			Rules = [UiValidationRule.Require("Custom failure.", () => false)],
		};

		Assert.Multiple(() =>
		{
			// The length is reported, not the rule: the declared constraints come first.
			Assert.That(failing.Validate().Resolved(), Is.EqualTo("Field exceeds 2 characters"));
			Assert.That(ruleOnly.Validate().Resolved(), Is.EqualTo("Custom failure."));
		});
	}

	[Test]
	public void A_field_hidden_by_visible_when_is_skipped_by_validation_and_keeps_its_value()
	{
		var mode = new UiState<string>("simple");

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "mode", Binding = Bind.To(mode) },
					new UiStringInput
					{
						Key = "token",
						Required = true,
						Binding = Bind.ReadOnly(UiValue.Of(string.Empty)),
						VisibleWhen = UiValue.Of(new UiVisibleWhen
						{
							ParameterName = "mode",
							// Case-insensitive by contract, so a picked option value and a hand-typed one agree.
							Values = ["ADVANCED"],
							SiblingValue = () => mode.Value,
						}),
					},
				],
			});

		var hidden = FindById(view.Tree.Root, "token")!;

		Assert.Multiple(() =>
		{
			Assert.That(hidden.Properties.ContainsKey(UiConfigProperties.Invalid),
				Is.False,
				"a hidden required field must not be able to block a submit");
			Assert.That(hidden.Properties.ContainsKey(UiConfigProperties.VisibleWhen),
				Is.True,
				"the condition itself stays on the node - the renderer is what hides the field");
			Assert.That(hidden.Properties.ContainsKey(UiConfigProperties.Value),
				Is.True,
				"a hidden field keeps its value and is still submitted");
		});

		view.DrainPatches();
		mode.Value = "advanced";

		var patches = view.DrainPatches();
		var shown = FindById(view.Tree.Root, "token")!;

		Assert.Multiple(() =>
		{
			Assert.That(shown.Properties[UiConfigProperties.Invalid].GetBoolean(), Is.True);
			// The property carries a reference, not text, so the same node renders in whichever language
			// the reader is on - which is what lets a language change take effect without the producer
			// rebuilding the tree.
			Assert.That(shown.Properties[UiConfigProperties.ValidationMessage].ResolvedText(),
				Is.EqualTo("token is required"));
			Assert.That(shown.Properties[UiConfigProperties.ValidationMessage].ResolvedText("de"),
				Is.EqualTo("token ist erforderlich"));
			Assert.That(patches.SelectMany(patch => patch.Operations)
					.Where(operation => string.Equals(operation.NodeId, "token", StringComparison.Ordinal))
					.Select(operation => operation.Op),
				Is.EqualTo(new[] { UiPatchOperations.SetProperties }).AsCollection);
		});
	}

	[Test]
	public void A_field_with_no_constraints_carries_no_validation_keys_at_all()
	{
		var text = new UiState<string>("a");

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "free", Binding = Bind.To(text) },
					new UiStringInput { Key = "constrained", Required = true, Binding = Bind.ReadOnly(UiValue.Of("")) },
				],
			});

		var free = FindById(view.Tree.Root, "free")!;
		var constrained = FindById(view.Tree.Root, "constrained")!;

		view.DrainPatches();
		text.Value = "b";

		var patches = view.DrainPatches();

		Assert.That(patches, Has.Count.EqualTo(1));
		Assert.That(patches[0].Operations, Has.Count.EqualTo(1));

		Assert.Multiple(() =>
		{
			Assert.That(free.Properties.ContainsKey(UiConfigProperties.Invalid), Is.False);
			Assert.That(free.Properties.ContainsKey(UiConfigProperties.ValidationMessage), Is.False);
			Assert.That(constrained.Properties.ContainsKey(UiConfigProperties.Invalid), Is.True);

			// The point of the conditional declaration: a keystroke on an unconstrained field dirties exactly
			// one cell, so it emits exactly one property change rather than three.
			Assert.That(patches[0].Operations[0].Properties, Is.Not.Null);
			Assert.That(patches[0].Operations[0].Properties!.Keys,
				Is.EqualTo(new[] { UiConfigProperties.Value }).AsCollection);
		});
	}

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
}
