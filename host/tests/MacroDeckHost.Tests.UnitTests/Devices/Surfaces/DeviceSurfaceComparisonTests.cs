using System.Reflection;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

/// <summary>
/// The push rule is only as good as this comparison: a field the surface carries but the comparison
/// ignores is a change the device silently never receives. The properties are therefore enumerated
/// rather than trusted, so adding one to the surface without teaching the comparison about it fails
/// here instead of in someone's hardware.
/// </summary>
[TestFixture]
internal sealed class DeviceSurfaceComparisonTests
{
	private static readonly DeviceSurfaceWidget _widget = new()
	{
		Id = "w1",
		Type = "ActionButton",
		PositionX = 0,
		PositionY = 0,
		Width = 1,
		Height = 1,
		SupportedInteractions = [DeviceInteractionKind.Press],
		Appearance = new DeviceSurfaceAppearance()
	};

	private static readonly DeviceSurface _surface = new()
	{
		Revision = 1,
		Profile = new DeviceSurfaceProfile("p", "P"),
		Folder = new DeviceSurfaceFolder("f", "F", null, true),
		Layout = new DeviceSurfaceLayout { Rows = 2, Columns = 3 },
		Widgets = [_widget]
	};

	private static IEnumerable<PropertyInfo> WidgetProperties => Properties(typeof(DeviceSurfaceWidget));

	private static IEnumerable<PropertyInfo> AppearanceProperties => Properties(typeof(DeviceSurfaceAppearance));

	[TestCaseSource(nameof(WidgetProperties))]
	public void Every_widget_property_is_compared(PropertyInfo property)
	{
		var changed = _widget with { };
		Mutate(changed, property);

		Assert.That(SameWith(changed),
			Is.False,
			$"DeviceSurfaceComparison ignores DeviceSurfaceWidget.{property.Name}");
	}

	[TestCaseSource(nameof(AppearanceProperties))]
	public void Every_appearance_property_is_compared(PropertyInfo property)
	{
		var appearance = _widget.Appearance! with { };
		Mutate(appearance, property);

		Assert.That(SameWith(_widget with { Appearance = appearance }),
			Is.False,
			$"DeviceSurfaceComparison ignores DeviceSurfaceAppearance.{property.Name}");
	}

	[Test]
	public void An_unchanged_surface_compares_equal()
		=> Assert.That(DeviceSurfaceComparison.SameContent(_surface, _surface with { Revision = 7 }), Is.True);

	private static bool SameWith(DeviceSurfaceWidget widget)
		=> DeviceSurfaceComparison.SameContent(_surface, _surface with { Widgets = [widget] });

	private static IEnumerable<PropertyInfo> Properties(Type type)
		=> type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.CanWrite || property.SetMethod is not null);

	/// <summary>Replaces the property's value with one that differs from the fixture's.</summary>
	private static void Mutate(object target, PropertyInfo property)
	{
		var value = property.GetValue(target);
		property.SetValue(target,
			property.PropertyType switch
			{
				var type when type == typeof(string) => value is null ? "changed" : $"{value}-changed",
				var type when type == typeof(int) => (int)value! + 1,
				var type when type == typeof(int?) => (value as int? ?? 0) + 1,
				var type when type == typeof(double?) => (value as double? ?? 0) + 1,
				var type when type == typeof(bool) => !(bool)value!,
				var type when type == typeof(DeviceSurfaceAppearance) => null,
				var type when type == typeof(IReadOnlyList<DeviceInteractionKind>)
					=> new[] { DeviceInteractionKind.LongPress, DeviceInteractionKind.Release },
				var type when type == typeof(IReadOnlyDictionary<string, string>)
					=> new Dictionary<string, string>(StringComparer.Ordinal) { ["changed"] = "1" },
				_ => throw new NotSupportedException(
					$"{property.DeclaringType?.Name}.{property.Name} is a {property.PropertyType.Name}, which this " +
					"test does not know how to change - teach it, and teach DeviceSurfaceComparison too.")
			});
	}
}
