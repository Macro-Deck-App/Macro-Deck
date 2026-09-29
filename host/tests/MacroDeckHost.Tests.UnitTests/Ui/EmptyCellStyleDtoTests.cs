using System.Text.Json;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class EmptyCellStyleDtoTests
{
	[TestCase(EmptyCellStyle.Transparent, "transparent")]
	[TestCase(EmptyCellStyle.Visible, "visible")]
	[TestCase(null, null)]
	public void Folders_and_profiles_carry_the_style_as_a_lowercase_string(EmptyCellStyle? style, string? expected)
	{
		var folder = FolderDtoMapper.MapToDto(new FolderEntity
		{
			Id = Guid.NewGuid(), ProfileId = Guid.NewGuid(), Name = "F", Order = 0, EmptyCellStyle = style
		});
		var profile = ProfileDtoMapper.MapJsonProfile(new ProfileEntity
		{
			Id = Guid.NewGuid(), Name = "P", DefaultEmptyCellStyle = style
		});

		Assert.Multiple(() =>
		{
			Assert.That(folder.EmptyCellStyle, Is.EqualTo(expected));
			Assert.That(profile.DefaultEmptyCellStyle, Is.EqualTo(expected));
		});
	}

	[Test]
	public void An_unset_style_is_left_off_the_wire()
	{
		var folder = FolderDtoMapper.MapToDto(new FolderEntity
		{
			Id = Guid.NewGuid(), ProfileId = Guid.NewGuid(), Name = "F", Order = 0
		});

		Assert.That(JsonSerializer.Serialize(folder, UiWebSocketProtocol.Json), Does.Not.Contain("emptyCellStyle"));
	}
}
