namespace MacroDeck.LicenseTool.Licensing;

internal sealed record SelectedLicense(string Id, string? Exception)
{
	public override string ToString() => Exception is null ? Id : $"{Id} WITH {Exception}";
}

internal sealed class LicensePolicy(IReadOnlyList<string> allowedLicenses, IReadOnlyList<string> allowedExceptions)
{
	private sealed record Choice(IReadOnlyList<SelectedLicense> Licenses, int Rank);

	public IReadOnlyList<string> AllowedLicenses { get; } = allowedLicenses;

	public IReadOnlyList<SelectedLicense>? Select(string expression, IReadOnlyCollection<string> reviewedLicenses)
	{
		var parsed = SpdxExpression.Parse(expression);
		return Evaluate(parsed, reviewedLicenses)?.Licenses;
	}

	private Choice? Evaluate(SpdxExpression expression, IReadOnlyCollection<string> reviewedLicenses)
	{
		switch (expression)
		{
			case SpdxLicense license:
				return EvaluateLicense(license, reviewedLicenses);

			case SpdxAnd and:
			{
				var parts = and.Operands.Select(operand => Evaluate(operand, reviewedLicenses)).ToList();
				if (parts.Any(part => part is null))
				{
					return null;
				}

				var licenses = parts.SelectMany(part => part!.Licenses).Distinct().ToList();
				return new Choice(licenses, parts.Max(part => part!.Rank));
			}

			case SpdxOr or:
				return or.Operands
					.Select(operand => Evaluate(operand, reviewedLicenses))
					.Where(choice => choice is not null)
					.OrderBy(choice => choice!.Rank)
					.FirstOrDefault();

			default:
				return null;
		}
	}

	private Choice? EvaluateLicense(SpdxLicense license, IReadOnlyCollection<string> reviewedLicenses)
	{
		string? exception = null;
		if (license.Exception is not null)
		{
			exception = allowedExceptions.FirstOrDefault(allowed =>
				string.Equals(allowed, license.Exception, StringComparison.OrdinalIgnoreCase));
			if (exception is null)
			{
				return null;
			}
		}

		for (var index = 0; index < AllowedLicenses.Count; index++)
		{
			if (string.Equals(AllowedLicenses[index], license.Id, StringComparison.OrdinalIgnoreCase))
			{
				return new Choice([new SelectedLicense(AllowedLicenses[index], exception)], index);
			}
		}

		var reviewed = reviewedLicenses.FirstOrDefault(id =>
			string.Equals(id, license.Id, StringComparison.OrdinalIgnoreCase));
		return reviewed is null
			? null
			: new Choice([new SelectedLicense(reviewed, exception)], AllowedLicenses.Count);
	}
}
