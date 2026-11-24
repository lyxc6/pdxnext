using System;

namespace PDX.SDK.Contracts.Attributes;

public class LanguageAttribute : Attribute
{
	public string LanguageString { get; }

	public LanguageAttribute(string languageString)
	{
		LanguageString = languageString;
	}
}
