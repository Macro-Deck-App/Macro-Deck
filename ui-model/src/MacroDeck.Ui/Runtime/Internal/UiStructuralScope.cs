using MacroDeck.Ui.Dsl;

namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// The dependency-tracking scope around one structural decision - a <see cref="UiWhen" />'s condition and
/// content, a <see cref="UiRepeat{TItem}" />'s item list, key selection and templates.
///
/// <para>
/// It carries everything a later reconcile needs: the authored element, the id-composition context it was
/// materialized in, the run of its parent's child list it owns, and the decision it last produced. The last
/// decision is what makes an invalidation cheap in the common case - a condition that was re-evaluated to the
/// same boolean, or an item list whose keys are unchanged, contributes no operation at all.
/// </para>
/// </summary>
internal sealed class UiStructuralScope : UiDependent
{
	internal UiStructuralScope(
		UiElement element,
		string? structuralPrefix,
		string? inputScope,
		string parentDeclarationPath)
	{
		Element = element;
		StructuralPrefix = structuralPrefix;
		InputScope = inputScope;
		ParentDeclarationPath = parentDeclarationPath;
	}

	/// <summary>The <see cref="UiWhen" /> or <see cref="UiRepeat{TItem}" /> this scope decides for.</summary>
	internal UiElement Element { get; }

	/// <summary>The structural id prefix in force at this scope's position.</summary>
	internal string? StructuralPrefix { get; }

	/// <summary>The input-id scope in force at this scope's position.</summary>
	internal string? InputScope { get; }

	/// <summary>The diagnostic declaration path this scope's element was declared under.</summary>
	internal string ParentDeclarationPath { get; }

	/// <summary>The run of the parent node's child list this scope's content occupies. Assigned by
	/// <see cref="UiChildRegion.AddRegion" /> as the region is opened.</summary>
	internal UiChildRegion Region { get; set; } = null!;

	/// <summary>Whether this scope is already queued for reconciliation, so a state written twice before a
	/// flush queues it once.</summary>
	internal bool IsQueued { get; set; }

	/// <summary>The condition's result at the last materialization, for a <see cref="UiWhen" />.</summary>
	internal bool LastCondition { get; set; }

	/// <summary>
	/// The item list instance the last materialization saw, for a <see cref="UiRepeat{TItem}" />. Reference
	/// equality against it is the whole gate: the same instance means nothing happened, a different one re-runs
	/// the templates.
	///
	/// <para>
	/// The key sequence deliberately does not gate this. An item's element closes over the item instance, so
	/// replacing one item with a new record carrying the same key - the idiomatic edit for an immutable item
	/// type - changes what that item renders while leaving the keys identical. What keeps the resulting patch
	/// minimal is the id diff, not this comparison.
	/// </para>
	/// </summary>
	internal object? LastItemList { get; set; }

	/// <summary>Queues this scope for reconciliation on the owning view's next flush. A released scope stands
	/// for content that is no longer in the tree and must not queue anything.</summary>
	internal override void Invalidate()
	{
		if (IsReleased)
		{
			return;
		}

		View?.MarkStructural(this);
	}
}
