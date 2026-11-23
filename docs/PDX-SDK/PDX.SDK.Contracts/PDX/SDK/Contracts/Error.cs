using System;
using PDX.SDK.Contracts.Enums.Errors;

namespace PDX.SDK.Contracts;

public class Error
{
	public BaseCategory Category { get; }

	public Enum SubCategory { get; }

	public string Details { get; }

	public string Raw { get; }

	public Error(string raw)
	{
		Category = BaseCategory.Undefined;
		SubCategory = BaseCategory.Undefined;
		Details = "";
		Raw = raw;
	}

	public Error(Enum subCategory, string details, string raw)
	{
		string name = subCategory.GetType().Name;
		if (name == "BaseCategory")
		{
			Category = BaseCategory.Undefined;
			SubCategory = BaseCategory.Undefined;
			Details = "";
			Raw = raw;
		}
		else
		{
			Category = (BaseCategory)Enum.Parse(typeof(BaseCategory), name, ignoreCase: true);
			SubCategory = subCategory;
			Details = details;
			Raw = raw;
		}
	}

	public Error(string categoryString, string subCategoryString, string details, string raw)
	{
		try
		{
			Category = (BaseCategory)Enum.Parse(typeof(BaseCategory), categoryString, ignoreCase: true);
			try
			{
				string typeName = "PDX.SDK.Contracts.Enums.Errors." + Category;
				SubCategory = (Enum)Enum.Parse(Type.GetType(typeName), subCategoryString, ignoreCase: true);
			}
			catch (Exception)
			{
				SubCategory = BaseCategory.Undefined;
			}
		}
		catch (Exception)
		{
			Category = BaseCategory.Undefined;
			SubCategory = BaseCategory.Undefined;
		}
		Details = details;
		Raw = raw;
	}

	public static bool operator ==(Error a, BaseCategory b)
	{
		return a.Category == b;
	}

	public static bool operator !=(Error a, BaseCategory b)
	{
		return a.Category != b;
	}

	public static bool operator ==(BaseCategory a, Error b)
	{
		return b.Category == a;
	}

	public static bool operator !=(BaseCategory a, Error b)
	{
		return b.Category != a;
	}

	public static bool operator ==(Error a, Enum b)
	{
		if (b != null)
		{
			if (!(a.SubCategory.GetType() == b.GetType()))
			{
				return false;
			}
			return a.SubCategory.ToString() == b.ToString();
		}
		return (object)a == null;
	}

	public static bool operator !=(Error a, Enum b)
	{
		if (b != null)
		{
			if (!(a.SubCategory.GetType() == b.GetType()))
			{
				return true;
			}
			return a.SubCategory.ToString() != b.ToString();
		}
		return (object)a != null;
	}

	public static bool operator ==(Enum a, Error b)
	{
		if (a != null)
		{
			if (!(b.SubCategory.GetType() == a.GetType()))
			{
				return false;
			}
			return b.SubCategory.ToString() == a.ToString();
		}
		return (object)b == null;
	}

	public static bool operator !=(Enum a, Error b)
	{
		if (a != null)
		{
			if (!(b.SubCategory.GetType() == a.GetType()))
			{
				return true;
			}
			return b.SubCategory.ToString() != a.ToString();
		}
		return (object)b != null;
	}

	public override bool Equals(object obj)
	{
		return this == (Error)obj;
	}

	public override int GetHashCode()
	{
		return (Category.ToString() + SubCategory.ToString()).GetHashCode();
	}

	public override string ToString()
	{
		return $"{Category.ToString()}.{SubCategory.ToString()} - {Details} - {Raw}";
	}
}
