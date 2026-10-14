using System.Runtime.CompilerServices;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.EmailInbox;

internal static class EmailInboxWidgetIcons
{
	private const string MailName = "mail";

	private static readonly ConditionalWeakTable<IUiResourceStore, Lazy<UiResource>> _registrations = new();

	internal static UiResource EnsureRegistered(IUiResourceStore store)
	{
		ArgumentNullException.ThrowIfNull(store);

		return _registrations.GetValue(store,
				static registryStore => new Lazy<UiResource>(() => Register(registryStore)))
			.Value;
	}

	private static UiResource Register(IUiResourceStore store)
	{
		var assembly = typeof(EmailInboxWidgetIcons).Assembly;
		var resourceName = Array.Find(assembly.GetManifestResourceNames(),
				candidate => candidate.EndsWith($".EmailInbox.Icons.{MailName}.svg", StringComparison.Ordinal))
			?? throw new InvalidOperationException($"The {MailName} icon is not embedded in "
				+ $"'{assembly.GetName().Name}'.");

		using var stream = assembly.GetManifestResourceStream(resourceName)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);

		return store.Register(new UiResourceRegistration
		{
			OwnerId = EmailWidgetTypes.OwnerId,
			Name = MailName,
			MediaType = "image/svg+xml",
			Content = memory.ToArray()
		});
	}
}
