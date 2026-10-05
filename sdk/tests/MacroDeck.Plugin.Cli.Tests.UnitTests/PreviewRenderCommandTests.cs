using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Cli.Rendering;
using MacroDeck.Plugin.Cli.Runtime;
using MacroDeck.Plugin.Cli.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

[TestFixture]
public class PreviewRenderCommandTests
{
	private static readonly string[] _stationTileSizes = ["station-tile-200x200.png", "station-tile-252x120.png"];
	private static readonly string[] _oneCellStationTile = ["station-tile-120x120.png"];
	private static readonly string[] _defaultViews =
		["setalertthresholdaction-default-120x120.png", "wellbehavedconfigflow-default-120x120.png"];
	private static readonly byte[] _black = [0, 0, 0];

	private string _output = null!;

	[SetUp]
	public void CreateOutputDirectory()
		=> _output = Path.Combine(Path.GetTempPath(), "mdpreview-" + Guid.NewGuid().ToString("N"));

	[TearDown]
	public void DeleteOutputDirectory()
	{
		if (Directory.Exists(_output))
		{
			Directory.Delete(_output, recursive: true);
		}
	}

	[Test]
	public async Task Exactly_one_subject_selector_is_required()
	{
		var (_, error, exitCode) = await CliRunner.Run("preview", "render");

		Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
		Assert.That(error, Does.Contain("invalid-selector-count"));
	}

	[TestCase("--size", "200", "invalid-size")]
	[TestCase("--size", "0x10", "invalid-size")]
	[TestCase("--cells", "axb", "invalid-size")]
	[TestCase("--scale", "0", "invalid-scale")]
	[TestCase("--scale", "9", "invalid-scale")]
	[TestCase("--radius", "-1", "invalid-radius")]
	[TestCase("--background", "url(x)", "invalid-background")]
	public async Task A_bad_option_value_is_a_usage_error_with_its_own_code(string option, string value, string code)
	{
		var (_, error, exitCode) = await CliRunner.Run("preview", "render", "--executable", "x", option, value);

		Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
		Assert.That(error, Does.Contain(code));
	}

	[Test]
	public async Task An_unknown_theme_is_rejected_by_the_parser()
	{
		var (_, _, exitCode) = await CliRunner.Run("preview", "render", "--executable", "x", "--theme", "sepia");

		Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
	}

	[Test]
	public async Task A_browser_that_does_not_exist_is_an_environment_problem()
	{
		var (_, error, exitCode) = await CliRunner.Run("preview", "render", "--executable", FixturePlugins.WellBehaved(),
			"--browser", Path.Combine(_output, "no-chrome"));

		Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
		Assert.That(error, Does.Contain("browser-not-found"));
	}

	[Test]
	public async Task Every_scenario_is_rendered_at_every_requested_size_with_the_requested_look()
	{
		var screenshotter = new FakeScreenshotter();

		var (_, _, exitCode) = await Render(screenshotter, "--size", "200x200", "--cells", "2x1", "--scale", "3",
			"--theme", "light", "--background", "#112233", "--radius", "5", "--locale", "de-DE");

		var tile = screenshotter.Shots.Where(shot => Path.GetFileName(shot.OutputPath).StartsWith("station-tile-", StringComparison.Ordinal)).ToList();
		var scene = JsonDocument.Parse(tile[0].SceneJson).RootElement;

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(tile.Select(shot => Path.GetFileName(shot.OutputPath)),
				Is.EqualTo(_stationTileSizes));
			Assert.That(tile.Select(shot => shot.Scale), Is.All.EqualTo(3));
			Assert.That(scene.GetProperty("theme").GetString(), Is.EqualTo("light"));
			Assert.That(scene.GetProperty("background").GetString(), Is.EqualTo("#112233"));
			Assert.That(scene.GetProperty("radius").GetInt32(), Is.EqualTo(5));
			Assert.That(scene.GetProperty("locale").GetString(), Is.EqualTo("de-DE"));
			Assert.That(scene.GetProperty("root").GetProperty("id").GetString(), Is.EqualTo("station"));
			Assert.That(screenshotter.Shots.GroupBy(shot => Path.GetFileName(shot.OutputPath)[..^"-WxHxxx.png".Length]).All(group => group.Count() == 2),
				Is.True);
		});
	}

	[Test]
	public async Task Without_a_size_a_widget_is_rendered_at_one_deck_cell()
	{
		var screenshotter = new FakeScreenshotter();

		await Render(screenshotter, "--preview", "Station tile");

		Assert.That(screenshotter.Shots.Select(shot => Path.GetFileName(shot.OutputPath)), Is.EqualTo(_oneCellStationTile));
	}

	[Test]
	public async Task The_preview_option_selects_scenarios_by_name_and_reports_an_unknown_one()
	{
		var screenshotter = new FakeScreenshotter();

		var (_, error, exitCode) = await Render(screenshotter, "--preview", "no such scenario");

		Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
		Assert.That(error, Does.Contain("unknown-preview"));
		Assert.That(screenshotter.Shots, Is.Empty);
	}

	[Test]
	public async Task Scenarios_with_the_same_name_in_two_views_do_not_overwrite_each_other()
	{
		var screenshotter = new FakeScreenshotter();

		await Render(screenshotter, "--preview", "Default");

		Assert.That(screenshotter.Shots.Select(shot => Path.GetFileName(shot.OutputPath)),
			Is.EquivalentTo(_defaultViews));
	}

	[TestCase("en-US", "Connect", "{count} devices found")]
	[TestCase("de-DE", "Verbinden", "{count} Geräte gefunden")]
	[TestCase("de", "Verbinden", "{count} Geräte gefunden")]
	[TestCase("fr-FR", "Connect", "{count} devices found")]
	public async Task The_plugin_text_is_resolved_for_the_requested_locale_and_falls_back_to_the_plugin_default(
		string locale, string connect, string devices)
	{
		var screenshotter = new FakeScreenshotter();

		var (_, error, exitCode) = await Render(screenshotter, "--preview", "Localized label", "--locale", locale);

		var translations = JsonDocument.Parse(screenshotter.Shots.Single().SceneJson).RootElement.GetProperty("translations")
			.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString());

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That(translations.Single(pair => pair.Key.EndsWith(":Connect", StringComparison.Ordinal)).Value, Is.EqualTo(connect));
			Assert.That(translations.Where(pair => pair.Key.Contains(":DeviceCount", StringComparison.Ordinal)).Select(pair => pair.Value),
				Does.Contain(devices));
			Assert.That(translations.Keys, Is.All.StartsWith("plugin:"));
		});
	}

	[Test]
	public async Task A_plugin_that_did_not_get_its_localization_capability_accepted_still_renders_with_no_translations()
	{
		var options = new MacroDeckTestHostOptions
		{
			NegotiateCapability = capability => capability.Kind == CapabilityKinds.Localization
				? CapabilityNegotiationResult.Reject(capability.Kind, "not offered")
				: CapabilityNegotiationResult.Accept(capability.Kind, 1)
		};
		await using var host = await MacroDeckTestHost.StartAsync(options);
		await using var plugin = await host.LaunchAsync(PluginSubjectResolver.ResolveExecutable(FixturePlugins.WellBehaved()));
		var session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(60));
		var error = new StringWriter();

		var translations = await PreviewPluginCatalog.LoadAsync(new CliConsole(Verbosity.Normal, true, error: error), session, "de-DE");

		Assert.Multiple(() =>
		{
			Assert.That(translations, Is.Empty);
			Assert.That(error.ToString(), Is.Empty);
		});
	}

	[Test]
	public async Task A_preview_the_page_cannot_draw_is_skipped_with_a_warning_and_the_run_still_succeeds()
	{
		var screenshotter = new FakeScreenshotter
		{
			Fail = shot => shot.SceneJson.Contains("\"id\":\"station\"", StringComparison.Ordinal)
				? null
				: new PreviewRenderException("preview-unsupported", "step")
		};

		var (output, error, exitCode) = await Render(screenshotter);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(error, Does.Contain("preview-unsupported"));
			Assert.That(output, Does.Contain("Rendered 1 image(s)"));
			Assert.That(output, Does.Contain("skipped"));
		});
	}

	[Test]
	public async Task A_render_failure_makes_the_run_fail_but_the_other_images_are_still_written()
	{
		var screenshotter = new FakeScreenshotter
		{
			Fail = shot => shot.OutputPath.Contains("station-tile", StringComparison.Ordinal)
				? new PreviewRenderException("render-failed", "boom")
				: null
		};

		var (_, error, exitCode) = await Render(screenshotter);

		Assert.That(exitCode, Is.EqualTo(ExitCode.SubjectInvalid));
		Assert.That(error, Does.Contain("render-failed"));
		Assert.That(screenshotter.Shots, Has.Count.GreaterThan(1));
	}

	[Test]
	public async Task A_plugin_that_dies_while_starting_is_an_input_problem()
	{
		var previous = Environment.GetEnvironmentVariable("MACRODECK_MISBEHAVE");
		Environment.SetEnvironmentVariable("MACRODECK_MISBEHAVE", "exit-immediately");

		try
		{
			var (_, error, exitCode) = await CliRunner.RunPreview(new FakeScreenshotter().Factory, "preview", "render",
				"--executable", FixturePlugins.Misbehaving(), "--browser", FixturePlugins.Misbehaving(), "--output", _output);

			Assert.That(exitCode, Is.EqualTo(ExitCode.InputUnreadable));
			Assert.That(error, Does.Contain("subject-launch-failed"));
		}
		finally
		{
			Environment.SetEnvironmentVariable("MACRODECK_MISBEHAVE", previous);
		}
	}

	[Test]
	public async Task A_plugin_without_previews_has_nothing_to_render_and_that_is_not_an_error()
	{
		var screenshotter = new FakeScreenshotter();

		var (output, _, exitCode) = await CliRunner.RunPreview(screenshotter.Factory, "preview", "render",
			"--executable", FixturePlugins.Misbehaving(), "--browser", FixturePlugins.Misbehaving(), "--output", _output);

		Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
		Assert.That(output, Does.Contain("no [UiPreview] scenarios"));
		Assert.That(screenshotter.Shots, Is.Empty);
	}

	[Test]
	public async Task A_real_browser_draws_a_png_of_the_requested_size_with_transparent_corners()
	{
		var browser = RealBrowser();

		var (_, error, exitCode) = await CliRunner.Run("preview", "render", "--executable", FixturePlugins.WellBehaved(),
			"--preview", "Station tile", "--size", "200x200", "--scale", "2", "--background", "transparent", "--output", _output,
			"--browser", browser);

		var png = PngProbe.Read(Path.Combine(_output, "station-tile-200x200.png"));

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success), error);
			Assert.That((png.Width, png.Height), Is.EqualTo((400, 400)));
			Assert.That(png.ColorType, Is.EqualTo(6));
			Assert.That(png.FirstPixel[3], Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_radius_of_zero_leaves_the_corner_opaque()
	{
		var browser = RealBrowser();

		await CliRunner.Run("preview", "render", "--executable", FixturePlugins.WellBehaved(), "--preview", "Station tile",
			"--size", "200x200", "--radius", "0", "--output", _output, "--browser", browser);

		Assert.That(PngProbe.Read(Path.Combine(_output, "station-tile-200x200.png")).FirstPixel[0..3], Is.Not.EqualTo(_black));
	}

	[Test]
	public async Task The_light_theme_draws_a_lighter_tile_than_the_dark_one()
	{
		var browser = RealBrowser();

		foreach (var theme in new[] { "dark", "light" })
		{
			await CliRunner.Run("preview", "render", "--executable", FixturePlugins.WellBehaved(), "--preview", "Station tile",
				"--size", "200x200", "--radius", "0", "--theme", theme, "--output", Path.Combine(_output, theme), "--browser", browser);
		}

		var dark = PngProbe.Read(Path.Combine(_output, "dark", "station-tile-200x200.png")).FirstPixel[0];
		var light = PngProbe.Read(Path.Combine(_output, "light", "station-tile-200x200.png")).FirstPixel[0];

		Assert.That(light, Is.GreaterThan(dark));
	}

	[Test]
	public async Task The_tile_radius_scales_with_the_tile_like_the_deck_does()
	{
		var browser = RealBrowser();

		await CliRunner.Run("preview", "render", "--executable", FixturePlugins.WellBehaved(), "--preview", "Station tile",
			"--size", "240x240", "--scale", "1", "--radius", "20", "--output", _output, "--browser", browser);

		var png = PngProbe.Read(Path.Combine(_output, "station-tile-240x240.png"));

		Assert.Multiple(() =>
		{
			Assert.That(png.PixelAt(8, 8)[3], Is.EqualTo(0), "inside a 40 px corner");
			Assert.That(png.PixelAt(120, 120)[3], Is.EqualTo(255));
		});
	}

	[Test]
	public async Task A_video_stream_image_is_handed_to_every_scene_and_is_absent_without_the_option()
	{
		Directory.CreateDirectory(_output);
		var image = Path.Combine(_output, "camera.png");
		await File.WriteAllBytesAsync(image, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
		var with = new FakeScreenshotter();
		var without = new FakeScreenshotter();

		var (_, _, exitCode) = await Render(with, "--preview", "Station tile", "--size", "200x200", "--size", "100x100",
			"--video-stream-image", image);
		await Render(without, "--preview", "Station tile");

		var scenes = with.Shots.Select(shot => JsonDocument.Parse(shot.SceneJson).RootElement).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(scenes, Has.Count.EqualTo(2));
			Assert.That(scenes.Select(scene => scene.GetProperty("videoStreamImage").GetString()),
				Is.All.EqualTo("data:image/png;base64,iVBORw0KGgoBAgM="));
			Assert.That(JsonDocument.Parse(without.Shots.Single().SceneJson).RootElement.TryGetProperty("videoStreamImage", out _),
				Is.False);
		});
	}

	[TestCase("missing.png", null, "does not exist")]
	[TestCase("webp-as.jpg", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "not a PNG, JPEG or WebP")]
	[TestCase("notes.txt", new byte[] { 1, 2, 3 }, "must be a .png")]
	[TestCase("fake.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, "not a PNG, JPEG or WebP")]
	[TestCase("png-as.jpg", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "not a PNG, JPEG or WebP")]
	[TestCase("jpeg-as.png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "not a PNG, JPEG or WebP")]
	public async Task A_video_stream_image_that_is_not_a_usable_image_is_a_usage_error(string name, byte[]? content, string message)
	{
		Directory.CreateDirectory(_output);
		var image = Path.Combine(_output, name);

		if (content is not null)
		{
			await File.WriteAllBytesAsync(image, content);
		}

		var screenshotter = new FakeScreenshotter();

		var (_, error, exitCode) = await Render(screenshotter, "--video-stream-image", image);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("invalid-video-stream-image").And.Contain(message));
			Assert.That(screenshotter.Shots, Is.Empty);
		});
	}

	[TestCase("camera.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1 }, "data:image/jpeg;base64,/9j/4AE=")]
	[TestCase("camera.jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1 }, "data:image/jpeg;base64,/9j/4AE=")]
	[TestCase("camera.webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "data:image/webp;base64,UklGRgAAAABXRUJQ")]
	public async Task A_jpeg_or_webp_video_stream_image_is_accepted(string name, byte[] content, string dataUrl)
	{
		Directory.CreateDirectory(_output);
		var image = Path.Combine(_output, name);
		await File.WriteAllBytesAsync(image, content);
		var screenshotter = new FakeScreenshotter();

		var (_, _, exitCode) = await Render(screenshotter, "--preview", "Station tile", "--video-stream-image", image);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.Success));
			Assert.That(JsonDocument.Parse(screenshotter.Shots.Single().SceneJson).RootElement.GetProperty("videoStreamImage").GetString(),
				Is.EqualTo(dataUrl));
		});
	}

	[Test]
	public async Task A_video_stream_image_over_the_size_limit_is_a_usage_error()
	{
		Directory.CreateDirectory(_output);
		var image = Path.Combine(_output, "big.png");
		var bytes = new byte[8 * 1024 * 1024 + 1];
		bytes[0] = 0x89;
		await File.WriteAllBytesAsync(image, bytes);

		var (_, error, exitCode) = await Render(new FakeScreenshotter(), "--video-stream-image", image);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(ExitCode.UsageError));
			Assert.That(error, Does.Contain("invalid-video-stream-image").And.Contain("larger than 8 MB"));
		});
	}

	[Test]
	public async Task A_video_stream_is_drawn_as_the_still_image_by_a_real_browser_and_stays_empty_without_one()
	{
		var browser = RealBrowser();
		Directory.CreateDirectory(_output);
		var root = JsonDocument.Parse(
			"""{"id":"v","type":"macrodeck.video-stream","properties":{"stream":{"provider":"p","id":"s"},"fit":"cover"},"children":[]}""")
			.RootElement;
		var size = PreviewSize.OfCells(1, 1);
		var shotter = await ChromeScreenshotter.StartAsync(browser, CancellationToken.None);
		await using var _ = shotter.ConfigureAwait(false);

		foreach (var (name, still) in new[] { ("empty", null), ("still", SolidRedPng()) })
		{
			var options = new PreviewRenderOptions { Background = "#000000", Radius = 0 };
			await shotter.CaptureAsync(
				new PreviewShot(PreviewScene.Build(root, size, options, new Dictionary<string, string>(), new Dictionary<string, string>(), still),
					size, 1, Path.Combine(_output, name + ".png")),
				CancellationToken.None);
		}

		var empty = PngProbe.Read(Path.Combine(_output, "empty.png")).PixelAt(60, 60);
		var drawn = PngProbe.Read(Path.Combine(_output, "still.png")).PixelAt(60, 60);

		Assert.Multiple(() =>
		{
			Assert.That(drawn[0], Is.GreaterThan(200));
			Assert.That(drawn[1], Is.LessThan(50));
			Assert.That(empty[0], Is.LessThan(50));
		});
	}

	private static string SolidRedPng()
	{
		static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

		static byte[] Chunk(string type, byte[] data)
		{
			var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
			var crc = 0xFFFFFFFFu;

			foreach (var value in body)
			{
				crc ^= value;

				for (var bit = 0; bit < 8; bit++)
				{
					crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
				}
			}

			return [.. BigEndian((uint)data.Length), .. body, .. BigEndian(~crc)];
		}

		var raw = new List<byte>();

		for (var y = 0; y < 4; y++)
		{
			raw.Add(0);

			for (var x = 0; x < 4; x++)
			{
				raw.AddRange([255, 0, 0]);
			}
		}

		using var compressed = new MemoryStream();

		using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionMode.Compress, true))
		{
			zlib.Write(raw.ToArray());
		}

		var header = new byte[] { 0, 0, 0, 4, 0, 0, 0, 4, 8, 2, 0, 0, 0 };
		var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
			.Concat(Chunk("IHDR", header)).Concat(Chunk("IDAT", compressed.ToArray())).Concat(Chunk("IEND", []));

		return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
	}

	private async Task<(string Output, string Error, int ExitCode)> Render(FakeScreenshotter screenshotter, params string[] extra)
		=> await CliRunner.RunPreview(screenshotter.Factory,
			[.. new[] { "preview", "render", "--executable", FixturePlugins.WellBehaved(), "--browser", FixturePlugins.WellBehaved(), "--output", _output }, .. extra]);

	private static string RealBrowser()
		=> BrowserLocator.Find(null, Environment.GetEnvironmentVariable, File.Exists) ??
			Environment.GetEnvironmentVariable("CHROME_BIN") ??
			throw new IgnoreException("No Chrome, Chromium or Edge found: set MACRODECK_BROWSER to run the real-browser preview tests.");
}
