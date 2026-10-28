using System.Text.Json;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Secrets;

namespace MacroDeckHost.Tests.UnitTests.Portable;

public class PortableSecretReferencesTests
{
	private static readonly Guid _foreign = Guid.NewGuid();
	private static readonly Guid _carried = Guid.NewGuid();

	private static IEnumerable<TestCaseData> ForeignReferenceShapes()
	{
		yield return new TestCaseData($$$"""{"value":{"$secret":"{{{_foreign}}}"}}""").SetName("plain reference");
		yield return new TestCaseData($$$"""{"value":{"$secret":"{{{_foreign}}}","x":1}}""")
			.SetName("reference with a trailing property");
		yield return new TestCaseData($$$"""{"value":{"x":1,"$secret":"{{{_foreign}}}"}}""")
			.SetName("reference with a leading property");
		yield return new TestCaseData($$$"""{"value":{"$secret":"{{{_foreign}}}"}}""")
			.SetName("reference with an escaped key");
		yield return new TestCaseData(JsonSerializer.Serialize(new Dictionary<string, string>
			{
				["flows"] = $$$"""[{"children":[{"parameters":[{"value":{ "$secret" : "{{{_foreign}}}" }}]}]}]"""
			}))
			.SetName("reference inside flows stored as a string");
	}

	[TestCaseSource(nameof(ForeignReferenceShapes))]
	public void A_reference_to_a_secret_the_archive_does_not_carry_is_removed(string data)
	{
		var imported = PortableSecretReferences.KeepOnly(data, new HashSet<Guid> { _carried });

		Assert.That(Referenced(imported), Does.Not.Contain(_foreign));
	}

	[Test]
	public void A_reference_to_a_carried_secret_is_kept()
	{
		var data = $$$"""{"flows":[{"value":{"$secret":"{{{_carried}}}"}},{"value":{"$secret":"{{{_foreign}}}"}}]}""";

		var imported = PortableSecretReferences.KeepOnly(data, new HashSet<Guid> { _carried });

		Assert.That(Referenced(imported), Is.EqualTo(new[] { _carried }));
	}

	private static HashSet<Guid> Referenced(string? data)
	{
		var ids = new HashSet<Guid>(SecretReferences.Extract(data));
		using var document = JsonDocument.Parse(data!);
		Collect(document.RootElement, ids);
		return ids;
	}

	private static void Collect(JsonElement element, HashSet<Guid> ids)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var property in element.EnumerateObject())
				{
					if (property.Name == "$secret" && Guid.TryParse(property.Value.GetString(), out var id))
					{
						ids.Add(id);
					}

					Collect(property.Value, ids);
				}

				break;
			case JsonValueKind.Array:
				foreach (var item in element.EnumerateArray())
				{
					Collect(item, ids);
				}

				break;
			case JsonValueKind.String when element.GetString() is { } text && text.TrimStart().StartsWith('['):
				using (var nested = JsonDocument.Parse(text))
				{
					Collect(nested.RootElement, ids);
				}

				break;
		}
	}
}
