using System.Text.Json.Serialization;

namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// A package's self-declaration about artificial intelligence, shown with its Store listing. Shared by
/// the plugin manifest, the icon pack manifest and the Store registry, so every package kind declares the
/// same three facts the same way. Absent from a manifest means "not declared", which a store must never
/// present as "uses no AI"; every flag false and no <see cref="Services"/> is the explicit statement that
/// it uses none, while a service list without a flag still declares AI use. Declarative only: the host
/// never enforces or verifies it. A flag that is not a JSON boolean, or a service list that is not an array
/// or keeps no readable name, makes the whole declaration read as absent, and a malformed declaration never
/// stops a package from installing or running.
/// </summary>
[JsonConverter(typeof(PackageAiDeclarationConverter))]
public sealed record PackageAiDeclaration
{
	public const int MaxServices = 16;

	public const int MaxServiceLength = 64;

	/// <summary>Users interact with an AI system through the package, for example a chatbot, a voice
	/// assistant or a conversational agent.</summary>
	public bool Interaction { get; init; }

	/// <summary>The package generates images, audio, video or text with AI while it runs.</summary>
	public bool GeneratedContent { get; init; }

	/// <summary>The package ships assets, such as icons, images, sounds or texts, that were created or
	/// substantially changed with AI.</summary>
	public bool GeneratedAssets { get; init; }

	/// <summary>Display names of the external AI services the package uses, such as a model provider,
	/// in declared order. Trimmed, blank and duplicate entries dropped, at most <see cref="MaxServices"/>
	/// of at most <see cref="MaxServiceLength"/> characters. A store shows them as plain text, never as
	/// links.</summary>
	public IReadOnlyList<string>? Services { get; init; }
}
