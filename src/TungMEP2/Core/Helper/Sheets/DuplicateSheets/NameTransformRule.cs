namespace VP_RevitAPI.Helper.Sheets.DuplicateSheets;

public sealed class NameTransformRule
{
	public string Find { get; set; } = string.Empty;

	public string Replace { get; set; } = string.Empty;

	public string Prefix { get; set; } = string.Empty;

	public string Suffix { get; set; } = string.Empty;

	public string Apply(string source)
	{
		string input = source ?? string.Empty;
		string replaced = (string.IsNullOrEmpty(Find) ? input : input.Replace(Find, Replace));
		return Prefix + replaced + Suffix;
	}
}
