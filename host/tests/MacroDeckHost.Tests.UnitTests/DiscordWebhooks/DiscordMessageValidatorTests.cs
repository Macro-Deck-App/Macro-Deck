using System.Globalization;
using MacroDeckHost.Integrations.DiscordWebhooks.Models;

namespace MacroDeckHost.Tests.UnitTests.DiscordWebhooks;

[TestFixture]
internal sealed class DiscordMessageValidatorTests
{
	private static IReadOnlyList<DiscordViolation> Validate(
		Dictionary<string, string> map,
		bool allowTemplates = false)
	{
		return DiscordMessageValidator.Validate(DiscordMessageMap.Parse(map), allowTemplates);
	}

	private static Dictionary<string, string> WithField(string key, int length)
	{
		var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["content"] = "x" };
		if (key.StartsWith("embeds.0.fields.0.", StringComparison.Ordinal))
		{
			map["embeds.0.fields.0.name"] = "n";
			map["embeds.0.fields.0.value"] = "v";
		}
		else if (key.StartsWith("embeds.0.", StringComparison.Ordinal))
		{
			map["embeds.0.title"] = "t";
		}

		map[key] = new string('a', length);
		return map;
	}

	[TestCase("content", 2000)]
	[TestCase("username", 80)]
	[TestCase("embeds.0.title", 256)]
	[TestCase("embeds.0.description", 4096)]
	[TestCase("embeds.0.author.name", 256)]
	[TestCase("embeds.0.footer.text", 2048)]
	[TestCase("embeds.0.fields.0.name", 256)]
	[TestCase("embeds.0.fields.0.value", 1024)]
	public void A_value_at_the_discord_limit_passes_and_one_character_more_fails(string key, int limit)
	{
		Assert.Multiple(() =>
		{
			Assert.That(Validate(WithField(key, limit)), Is.Empty);
			Assert.That(Validate(WithField(key, limit + 1)), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void A_message_without_content_or_embed_is_refused()
	{
		Assert.That(Validate(new Dictionary<string, string> { ["username"] = "Deck" }).Select(v => v.Key),
			Does.Contain("empty"));
	}

	[Test]
	public void Ten_embeds_pass_and_eleven_fail()
	{
		Dictionary<string, string> Embeds(int count)
		{
			return Enumerable.Range(0, count)
				.ToDictionary(i => string.Create(CultureInfo.InvariantCulture, $"embeds.{i}.title"), _ => "t");
		}

		Assert.Multiple(() =>
		{
			Assert.That(Validate(Embeds(10)), Is.Empty);
			Assert.That(Validate(Embeds(11)).Select(v => v.Key), Does.Contain("embeds"));
		});
	}

	[Test]
	public void Twenty_five_fields_pass_and_twenty_six_fail()
	{
		Dictionary<string, string> Fields(int count)
		{
			var map = new Dictionary<string, string>(StringComparer.Ordinal);
			for (var i = 0; i < count; i++)
			{
				map[string.Create(CultureInfo.InvariantCulture, $"embeds.0.fields.{i}.name")] = "n";
				map[string.Create(CultureInfo.InvariantCulture, $"embeds.0.fields.{i}.value")] = "v";
			}

			return map;
		}

		Assert.Multiple(() =>
		{
			Assert.That(Validate(Fields(25)), Is.Empty);
			Assert.That(Validate(Fields(26)).Select(v => v.Key), Does.Contain("embed-0-fields"));
		});
	}

	[Test]
	public void All_embeds_together_may_hold_six_thousand_characters_of_text()
	{
		Dictionary<string, string> Text(int last)
		{
			return new Dictionary<string, string>
			{
				["embeds.0.description"] = new('a', 4000),
				["embeds.1.description"] = new('b', last)
			};
		}

		Assert.Multiple(() =>
		{
			Assert.That(Validate(Text(2000)), Is.Empty);
			Assert.That(Validate(Text(2001)).Select(v => v.Key), Does.Contain("embed-text"));
		});
	}

	[TestCase("Clyde Bot")]
	[TestCase("my discord hook")]
	public void A_username_naming_clyde_or_discord_is_refused(string username)
	{
		Assert.That(Validate(new Dictionary<string, string> { ["content"] = "x", ["username"] = username })
				.Select(v => v.Key),
			Does.Contain("username-forbidden"));
	}

	[Test]
	public void Colours_urls_and_ids_must_be_well_formed()
	{
		var keys = Validate(new Dictionary<string, string>
			{
				["embeds.0.title"] = "t",
				["embeds.0.color"] = "not a colour",
				["embeds.0.url"] = "ftp://example.invalid",
				["embeds.0.image"] = "file:///etc/passwd",
				["allowed.users"] = "123, abc"
			})
			.Select(v => v.Key)
			.ToList();

		Assert.That(keys,
			Is.SupersetOf(new[] { "embed-0-color", "embed-0-url-invalid", "embed-0-image-invalid", "users-invalid" }));
	}

	[Test]
	public void An_image_may_reference_an_attachment()
	{
		Assert.That(Validate(new Dictionary<string, string>
			{
				["embeds.0.title"] = "t", ["embeds.0.image"] = "attachment://chart.png"
			}),
			Is.Empty);
	}

	[Test]
	public void Pinging_every_user_conflicts_with_an_explicit_user_list()
	{
		Assert.That(Validate(new Dictionary<string, string>
				{
					["content"] = "x", ["allowed.parse"] = "users", ["allowed.users"] = "123"
				})
				.Select(v => v.Key),
			Does.Contain("users-conflict"));
	}

	[Test]
	public void At_most_one_hundred_role_ids_may_be_allowed()
	{
		Dictionary<string, string> Roles(int count)
		{
			return new Dictionary<string, string>
			{
				["content"] = "x",
				["allowed.roles"] = string.Join(",",
					Enumerable.Range(1, count)
						.Select(i =>
							i.ToString(CultureInfo.InvariantCulture)))
			};
		}

		Assert.Multiple(() =>
		{
			Assert.That(Validate(Roles(100)), Is.Empty);
			Assert.That(Validate(Roles(101)).Select(v => v.Key), Does.Contain("roles-count"));
		});
	}

	[Test]
	public void The_editor_accepts_templates_whose_text_only_exists_when_the_action_runs()
	{
		var map = new Dictionary<string, string>
		{
			["embeds.0.title"] = "t", ["embeds.0.color"] = "{{ vars.colour }}", ["embeds.0.image"] = "{{ vars.cover }}"
		};

		Assert.Multiple(() =>
		{
			Assert.That(Validate(map, true), Is.Empty);
			Assert.That(Validate(map),
				Has.Count.EqualTo(2),
				"a value that is still a template after rendering is invalid");
		});
	}

	[Test]
	public void A_templated_id_with_spaces_is_one_mention_in_the_editor()
	{
		var map = new Dictionary<string, string>
		{
			["content"] = "hi",
			["allowed.users"]
				= "{{ vars.streamer_id }}, 123456789012345678, {% if vars.mod %}{{ vars.mod_id }}{% endif %}, {{\n vars.guest_id }}"
		};

		Assert.That(Validate(map, true), Is.Empty);
	}
}
