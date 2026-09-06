using System.Reflection;
using System.Text.Json;
using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordVoiceSettingsReconcilerTests
{
	private static readonly string[] _muteAndDeaf = ["mute", "deaf"];

	private static readonly string[] _noiseSuppressionInputModeInOrder =
		["noise_suppression", "input.volume", "mode.type"];

	[Test]
	public void A_response_that_echoes_everything_is_confirmed()
	{
		var patch = new DiscordVoiceSettingsPatch { Mute = true, NoiseSuppression = false };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch,
			Parse("""{"mute":true,"noise_suppression":false}"""));

		Assert.That(unapplied, Is.Empty);
	}

	[Test]
	public void A_field_the_response_omits_is_reported_as_omitted()
	{
		var patch = new DiscordVoiceSettingsPatch { NoiseSuppression = true };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch, Parse("""{"mute":false}"""));

		var field = unapplied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(field.Field, Is.EqualTo("noise_suppression"));
			Assert.That(field.Reason, Is.EqualTo(DiscordFieldMismatch.Omitted));
		});
	}

	[Test]
	public void A_field_returned_with_the_old_value_is_reported_as_unchanged()
	{
		var patch = new DiscordVoiceSettingsPatch { EchoCancellation = true };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch,
			Parse("""{"echo_cancellation":false}"""));

		var field = unapplied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(field.Field, Is.EqualTo("echo_cancellation"));
			Assert.That(field.Reason, Is.EqualTo(DiscordFieldMismatch.Unchanged));
		});
	}

	[Test]
	public void An_empty_response_object_omits_every_named_field()
	{
		var patch = new DiscordVoiceSettingsPatch { Mute = true, Deaf = true };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch, Parse("{}"));

		Assert.That(unapplied.Select(f => f.Reason), Is.All.EqualTo(DiscordFieldMismatch.Omitted));
		Assert.That(unapplied.Select(f => f.Field), Is.EquivalentTo(_muteAndDeaf));
	}

	[Test]
	public void A_non_object_response_omits_every_field_the_patch_names()
	{
		var patch = new DiscordVoiceSettingsPatch { Mute = true, Qos = false };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch, Parse("null"));

		Assert.That(unapplied, Has.Count.EqualTo(2));
		Assert.That(unapplied.Select(f => f.Reason), Is.All.EqualTo(DiscordFieldMismatch.Omitted));
	}

	[Test]
	public void Only_the_fields_the_patch_names_are_reconciled()
	{
		var patch = new DiscordVoiceSettingsPatch { Mute = true };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch,
			Parse("""{"mute":true,"deaf":true,"echo_cancellation":true}"""));

		Assert.That(unapplied, Is.Empty);
	}

	[Test]
	public void An_empty_patch_is_confirmed()
	{
		var unapplied =
			DiscordVoiceSettingsReconciler.FindUnappliedFields(new DiscordVoiceSettingsPatch(), Parse("{}"));

		Assert.That(unapplied, Is.Empty);
	}

	[Test]
	public void Input_volume_is_read_out_of_the_input_device_object()
	{
		var patch = new DiscordVoiceSettingsPatch { Input = new DiscordVoiceDevicePatch { Volume = 60 } };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch,
			Parse("""{"input":{"volume":60}}"""));

		Assert.That(unapplied, Is.Empty);
	}

	[Test]
	public void A_volume_discord_rounded_by_under_half_a_percent_counts_as_applied()
	{
		var patch = new DiscordVoiceSettingsPatch { Input = new DiscordVoiceDevicePatch { Volume = 60 } };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch,
			Parse("""{"input":{"volume":60.3}}"""));

		Assert.That(unapplied, Is.Empty);
	}

	[Test]
	public void A_volume_discord_clamped_does_not_count_as_applied()
	{
		var patch = new DiscordVoiceSettingsPatch { Output = new DiscordVoiceDevicePatch { Volume = 150 } };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch,
			Parse("""{"output":{"volume":100}}"""));

		var field = unapplied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(field.Field, Is.EqualTo("output.volume"));
			Assert.That(field.Reason, Is.EqualTo(DiscordFieldMismatch.Unchanged));
		});
	}

	[Test]
	public void The_voice_mode_is_compared_as_an_exact_ordinal_string()
	{
		var patch = new DiscordVoiceSettingsPatch { Mode = new DiscordVoiceModePatch { Type = "PUSH_TO_TALK" } };

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch,
			Parse("""{"mode":{"type":"VOICE_ACTIVITY"}}"""));

		var field = unapplied.Single();
		Assert.Multiple(() =>
		{
			Assert.That(field.Field, Is.EqualTo("mode.type"));
			Assert.That(field.Reason, Is.EqualTo(DiscordFieldMismatch.Unchanged));
		});
	}

	[Test]
	public void Reported_field_names_are_discords_wire_names()
	{
		var patch = new DiscordVoiceSettingsPatch
		{
			NoiseSuppression = true,
			Input = new DiscordVoiceDevicePatch { Volume = 50 },
			Mode = new DiscordVoiceModePatch { Type = "VOICE_ACTIVITY" }
		};

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch, Parse("{}"));

		Assert.That(unapplied.Select(f => f.Field), Is.EqualTo(_noiseSuppressionInputModeInOrder));
	}

	[Test]
	public void Every_patch_property_has_a_reconciler_branch()
	{
		var propertyCount = typeof(DiscordVoiceSettingsPatch)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Length;
		Assert.That(propertyCount, Is.EqualTo(9), "a patch property was added or removed; update this test too");

		var patch = new DiscordVoiceSettingsPatch
		{
			Mute = true,
			Deaf = true,
			NoiseSuppression = true,
			EchoCancellation = true,
			AutomaticGainControl = true,
			Qos = true,
			Input = new DiscordVoiceDevicePatch { Volume = 50 },
			Output = new DiscordVoiceDevicePatch { Volume = 50 },
			Mode = new DiscordVoiceModePatch { Type = "PUSH_TO_TALK" }
		};

		var unapplied = DiscordVoiceSettingsReconciler.FindUnappliedFields(patch, Parse("{}"));

		var fields = unapplied.Select(f => f.Field).ToArray();
		Assert.Multiple(() =>
		{
			Assert.That(fields, Has.Length.EqualTo(9));
			Assert.That(DiscordVoiceSettingsReconciler.NamedFields(patch), Is.EqualTo(fields));
			Assert.That(DiscordVoiceSettingsReconciler.FieldOrder, Is.EqualTo(fields));
		});
	}

	private static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}
