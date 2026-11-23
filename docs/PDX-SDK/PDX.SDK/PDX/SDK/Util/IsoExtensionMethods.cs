using System;
using System.Reflection;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Util;

public static class IsoExtensionMethods
{
	public static string ToIetfTagLanguageString(this Language? language)
	{
		return language?.ToIetfTagLanguageString();
	}

	public static string ToIetfTagLanguageString(this Language language)
	{
		return typeof(Language).GetField(language.ToString())?.GetCustomAttribute<LanguageAttribute>(inherit: false)?.LanguageString ?? language.ToString();
	}

	public static Language GetLanguage(string IETFTag)
	{
		if (Enum.TryParse<Language>(IETFTag?.Replace('-', '_'), out var result))
		{
			return result;
		}
		return Language.Undefined;
	}

	public static Country GetCountry(string iso31661alpha2code)
	{
		if (Enum.TryParse<Country>(iso31661alpha2code, out var result))
		{
			return result;
		}
		return Country.Undefined;
	}
}
