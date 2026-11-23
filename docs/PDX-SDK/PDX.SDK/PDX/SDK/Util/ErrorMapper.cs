using System;
using System.Collections.Generic;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums.Errors;

namespace PDX.SDK.Util;

public class ErrorMapper<T>
{
	private Dictionary<Enum, T> categoryMapper = new Dictionary<Enum, T>();

	public T this[Error key]
	{
		get
		{
			if (categoryMapper.ContainsKey(key.SubCategory))
			{
				return categoryMapper[key.SubCategory];
			}
			if (categoryMapper.ContainsKey(key.Category))
			{
				return categoryMapper[key.Category];
			}
			return default(T);
		}
	}

	public T this[Enum key]
	{
		get
		{
			if (key is BaseCategory)
			{
				if (categoryMapper.ContainsKey((BaseCategory)(object)key))
				{
					return categoryMapper[(BaseCategory)(object)key];
				}
				return default(T);
			}
			if (categoryMapper.ContainsKey(key))
			{
				return categoryMapper[key];
			}
			return default(T);
		}
	}

	public void AddMapping(BaseCategory categoryKey, T value)
	{
		categoryMapper.Add(categoryKey, value);
	}

	public void AddMapping(Enum subCategorykey, T value)
	{
		categoryMapper.Add(subCategorykey, value);
	}
}
