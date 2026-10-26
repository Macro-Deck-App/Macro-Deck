using System.Globalization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Testing.Tests.UnitTests.Host;

/// <summary>
/// A view may be written from any thread, so a test that reads the host while a timer or a process callback
/// writes state must only ever see the producer's real disagreements, never one invented by reading the patch
/// queue and the view's tree a flush apart.
/// </summary>
[TestFixture]
public class UiTestHostConcurrentWriteTests
{
	private const int Writes = 5000;

	[Test]
	public async Task Reading_the_host_while_another_thread_writes_the_view_reports_no_disagreement()
	{
		var state = new UiState<string>("0");
		var host = UiTestHost.Render(new UiFlow
		{
			Key = "setup",
			Children = [new UiStringInput { Key = "count", Binding = Bind.ReadOnly(UiValue.From(() => state.Value)) }]
		});

		var writer = Task.Run(() =>
		{
			for (var index = 1; index <= Writes; index++)
			{
				state.Value = index.ToString(CultureInfo.InvariantCulture);
			}
		});

		while (!writer.IsCompleted)
		{
			Assert.That(() => host.Tree, Throws.Nothing);
		}

		await writer;

		Assert.That(UiCanonicalJson.Serialize(host.Tree), Is.EqualTo(UiCanonicalJson.Serialize(host.View.Tree)));
	}
}
