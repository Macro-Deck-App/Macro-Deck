using System.Reflection;
using System.Runtime.Loader;
using MacroDeck.Localization;
using static MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support.LocalizationGeneratorTestHarness;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class LocalizationCatalogGenerationTests
{
	[Test]
	public void Every_key_keeps_its_own_text_in_every_culture_of_a_large_resource_set()
	{
		var keys = Keys(600);

		WithGeneratedClass(keys,
			type =>
			{
				var catalog = (ILocalizationCatalog)type.GetProperty("LocalizationCatalog")!.GetValue(null)!;

				Assert.Multiple(() =>
				{
					Assert.That(catalog.KeysOf("en"), Is.EquivalentTo(keys));
					Assert.That(catalog.KeysOf("de"), Is.EquivalentTo(keys));
					foreach (var key in keys)
					{
						Assert.That(catalog.TryGetTemplate("en", key, out var english) ? english : null,
							Is.EqualTo($"English {key}"));
						Assert.That(catalog.TryGetTemplate("de", key, out var german) ? german : null,
							Is.EqualTo($"Deutsch {key}"));
					}
				});
			});
	}

	[Test]
	public void The_largest_generated_method_does_not_grow_with_the_number_of_keys()
	{
		var smaller = 0;
		var larger = 0;

		WithGeneratedClass(Keys(300), type => smaller = LargestMethodIlSize(type));
		WithGeneratedClass(Keys(600), type => larger = LargestMethodIlSize(type));

		Assert.That(larger, Is.EqualTo(smaller));
	}

	private static List<string> Keys(int count)
	{
		return [.. Enumerable.Range(0, count).Select(index => $"Key{index:D4}")];
	}

	private static void WithGeneratedClass(List<string> keys, Action<Type> test)
	{
		var run = Run(new Dictionary<string, string>
		{
			[DefaultFileName] = Resx(string.Join("\n", keys.Select(key => Entry(key, $"English {key}")))),
			["Strings.de.resx"] = Resx(string.Join("\n", keys.Select(key => Entry(key, $"Deutsch {key}"))))
		});
		Assert.That(run.GeneratedSource, Is.Not.Null);

		var context = new AssemblyLoadContext("generated-localization", true);
		try
		{
			test(LoadGeneratedClass(run.GeneratedSource!, context));
		}
		finally
		{
			context.Unload();
		}
	}

	private static int LargestMethodIlSize(Type type)
	{
		return type.GetMethods(BindingFlags.Public
				| BindingFlags.NonPublic
				| BindingFlags.Static
				| BindingFlags.DeclaredOnly)
			.Max(method => method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0);
	}
}
