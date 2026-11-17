using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class TemplateClockRenderTests
{
	private static readonly TimeZoneInfo _india
		= TimeZoneInfo.CreateCustomTimeZone("Test India", TimeSpan.FromHours(5.5), "Test India", "Test India");

	private VariableTemplateRenderer _renderer = null!;

	[SetUp]
	public void SetUp()
	{
		var clock = new ManualTimeProvider
		{
			Now = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
			Zone = _india
		};
		_renderer = new VariableTemplateRenderer(new VariableRegistry(), clock);
	}

	[TestCase("{{ date.now | date.to_string '%H:%M' }}", "17:30")]
	[TestCase("{{ date.utc_now | date.to_string '%H:%M' }}", "12:00")]
	[TestCase("{{ 'now' | date: '%H:%M' }}", "17:30")]
	[TestCase("{{ 'Now' | date: '%H:%M' }}", "17:30")]
	[TestCase("{{ \"today\" | date: '%Y-%m-%d' }}", "2026-03-01")]
	[TestCase("{{ date 'now' }}", "01 Mar 2026")]
	[TestCase("{{ date.parse 'now' | date.to_string '%M' }}", "30")]
	[TestCase("{{ 'now' | date.parse_to_string '%H:%M' output_culture: 'en-US' }}", "17:30")]
	public void A_template_that_reads_the_time_gets_the_host_clock_in_its_time_zone_and_is_recorded(
		string template,
		string expected)
	{
		var reads = new HashSet<string>(StringComparer.Ordinal);

		var rendered = _renderer.RenderRecording(template, VariableScope.Global, null, reads);

		Assert.Multiple(() =>
		{
			Assert.That(rendered, Is.EqualTo(expected));
			Assert.That(reads, Does.Contain(TemplateClock.Name));
		});
	}

	[TestCase("{{ '2016-01-05' | date: '%Y-%m-%d' }}", "2016-01-05")]
	[TestCase("{{ date.parse '2016/01/05' | date.to_string '%d.%m.%Y' }}", "05.01.2016")]
	[TestCase("{{ '03 14, 2016' | date.parse_to_string '%b %d, %y' input_pattern: '%m %d, %Y' }}", "Mar 14, 16")]
	[TestCase("{{ 'now on' | upcase }}", "NOW ON")]
	[TestCase("{{ vars.missing }}x", "x")]
	public void A_template_that_does_not_read_the_time_renders_as_before_and_is_not_recorded(
		string template,
		string expected)
	{
		var reads = new HashSet<string>(StringComparer.Ordinal);

		var rendered = _renderer.RenderRecording(template, VariableScope.Global, null, reads);

		Assert.Multiple(() =>
		{
			Assert.That(rendered, Is.EqualTo(expected));
			Assert.That(reads, Does.Not.Contain(TemplateClock.Name));
		});
	}
}
