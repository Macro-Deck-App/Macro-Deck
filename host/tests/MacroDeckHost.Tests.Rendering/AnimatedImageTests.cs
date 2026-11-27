using System.Text.Json;
using MacroDeckHost.Rendering;

namespace MacroDeckHost.Tests.Rendering;

public sealed class AnimatedImageTests
{
	private static JsonElement FixtureFile =>
		Fixtures.Read(Path.Combine(Fixtures.RepoRoot, "ui-model", "fixtures", "animated-images", "fixtures.json"));

	public static IEnumerable<TestCaseData> Cases()
	{
		foreach (var fixture in FixtureFile.GetProperty("fixtures").EnumerateArray())
		{
			var name = fixture.GetProperty("name").GetString()!;
			yield return new TestCaseData(name).SetName($"animated image {name}");
		}
	}

	[TestCaseSource(nameof(Cases))]
	public async Task Picture_decodes_as_the_fixture_states(string name)
	{
		var fixture = FixtureFile.GetProperty("fixtures")
			.EnumerateArray()
			.Single(item => item.GetProperty("name").GetString() == name);

		var image = AnimatedImageDecoder.Decode(Convert.FromBase64String(fixture.GetProperty("bytes").GetString()!));

		Assert.That(image, Is.Not.Null);
		var expectedFrames = fixture.GetProperty("frames")
			.EnumerateArray()
			.Select(frame => frame.GetString()!)
			.ToList();
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(image!.Width, Is.EqualTo(fixture.GetProperty("width").GetInt32()), "width");
			Assert.That(image.Height, Is.EqualTo(fixture.GetProperty("height").GetInt32()), "height");
			Assert.That(image.Frames, Has.Count.EqualTo(fixture.GetProperty("frameCount").GetInt32()), "frame count");
			Assert.That(image.DurationsMs, Is.EqualTo(Integers(fixture.GetProperty("durationsMs"))), "durations");
			Assert.That(image.EffectiveDurationsMs,
				Is.EqualTo(Integers(fixture.GetProperty("effectiveDurationsMs"))),
				"effective durations");
			var plays = fixture.GetProperty("plays");
			Assert.That(image.Plays,
				Is.EqualTo(plays.ValueKind == JsonValueKind.Null ? null : plays.GetInt32()),
				"plays");
			var timeline = fixture.TryGetProperty("timeline", out var entries) ? entries.EnumerateArray().ToList() : [];
			foreach (var entry in timeline)
			{
				var at = entry.GetProperty("t").GetDouble();
				Assert.That(image.FrameAt(at), Is.EqualTo(entry.GetProperty("frame").GetInt32()), $"frame at {at} ms");
			}

			for (var index = 0; index < Math.Min(expectedFrames.Count, image.Frames.Count); index++)
			{
				Assert.That(Convert.ToBase64String(image.Frames[index]),
					Is.EqualTo(expectedFrames[index]),
					$"frame {index} pixels");
			}

			await Task.CompletedTask;
		});
	}

	private static List<int> Integers(JsonElement array)
	{
		return array.EnumerateArray().Select(item => item.GetInt32()).ToList();
	}
}
