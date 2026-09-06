using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A12 - the variable fakes reproduce the documented failure statuses, rather than a fake that always
/// reports success and hides a plugin that never handles <c>NotFound</c>.
/// </summary>
[TestFixture]
public class A12_VariableFakeTests
{
	[Test]
	public async Task FakeUserVariableApi_reproduces_the_documented_failure_statuses()
	{
		var api = new FakeUserVariableApi();
		api.SeedGlobal("counter", VariableType.Numeric, 10.0);
		api.SeedGlobal("owned", VariableType.Numeric, 1.0, ownedByIntegration: true);
		api.SeedGlobal("label", VariableType.Text, "hello");
		api.SeedGlobal("flag", VariableType.Boolean, false);
		api.SeedWidgetScoped("widget-1", "counter", VariableType.Numeric, 99.0);

		var neverSeeded = await api.ApplyAsync("never-seeded", null, UserVariableOperation.Set, "x");
		var notEditable = await api.ApplyAsync("owned", null, UserVariableOperation.Set, "2");
		var addOnText = await api.ApplyAsync("label", null, UserVariableOperation.Add, "1");
		var toggle = await api.ApplyAsync("flag", null, UserVariableOperation.Toggle, "this operand must be ignored");

		Assert.Multiple(() =>
		{
			Assert.That(neverSeeded.Status, Is.EqualTo(UserVariableWriteStatus.NotFound));
			Assert.That(notEditable.Status, Is.EqualTo(UserVariableWriteStatus.NotEditable));
			Assert.That(addOnText.Status, Is.EqualTo(UserVariableWriteStatus.InvalidValue));
			Assert.That(toggle.Status, Is.EqualTo(UserVariableWriteStatus.Applied));
			Assert.That(api.GetCurrentValue("flag"), Is.EqualTo(true));
		});

		Assert.Multiple(() =>
		{
			// A widget-scoped variable wins over a global one of the same name...
			Assert.That(api.GetCurrentValue("counter", "widget-1"), Is.EqualTo(99.0));
			// ...but a null owner resolves globally, even though a widget-scoped "counter" exists elsewhere.
			Assert.That(api.GetCurrentValue("counter", null), Is.EqualTo(10.0));
		});
	}

	[Test]
	public async Task FakeUserVariableApi_create_reproduces_the_documented_refusals()
	{
		var api = new FakeUserVariableApi();
		api.SeedGlobal("counter", VariableType.Numeric, 10.0);
		api.SeedWidget("widget-1");

		var duplicate = await api.CreateAsync("counter", null, VariableType.Numeric);
		var shadow = await api.CreateAsync("counter", "widget-1", VariableType.Numeric, "99");
		var unknownWidget = await api.CreateAsync("fresh", "no-such-widget", VariableType.Text);
		var badValue = await api.CreateAsync("count", null, VariableType.Numeric, "many");

		// Every assertion here is a refusal: a fake whose CreateAsync always succeeded would let a plugin
		// ship without ever handling UnknownWidget or AlreadyExists.
		Assert.Multiple(() =>
		{
			Assert.That(duplicate.Status, Is.EqualTo(UserVariableCreateStatus.AlreadyExists));
			Assert.That(api.GetCurrentValue("counter"), Is.EqualTo(10.0));

			Assert.That(shadow.Status, Is.EqualTo(UserVariableCreateStatus.Created));
			Assert.That(api.GetCurrentValue("counter", "widget-1"), Is.EqualTo(99m));
			Assert.That(api.GetCurrentValue("counter"), Is.EqualTo(10.0));

			Assert.That(unknownWidget.Status, Is.EqualTo(UserVariableCreateStatus.UnknownWidget));
			Assert.That(api.GetCurrentValue("fresh", "no-such-widget"), Is.Null);

			Assert.That(badValue.Status, Is.EqualTo(UserVariableCreateStatus.InvalidValue));
			Assert.That(api.GetCurrentValue("count"), Is.Null);
		});
	}

	[Test]
	public async Task FakeVariableApi_reproduces_the_plain_crud_contract()
	{
		var api = new FakeVariableApi();

		Assert.That(await api.GetByNameAsync("unknown"), Is.Null);
		Assert.DoesNotThrowAsync(async () => await api.DeleteAsync(Guid.NewGuid()));

		var handle = await api.CreateAsync("temp", VariableType.Numeric, 5.0);

		Assert.Multiple(() =>
		{
			Assert.That(api.Created, Has.Count.EqualTo(1));
			Assert.That(api.Created[0].Id, Is.EqualTo(handle.Id));
		});
	}
}
