using System;
using System.Reflection;
using System.Text;

namespace PDX.SDK.Internal.Util;

internal class GraphQLRequestBuilder
{
	private Type _requestClass;

	public GraphQLRequestBuilder(Type requestClass)
	{
		_requestClass = requestClass;
	}

	public string Build()
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendClassProperties(_requestClass, stringBuilder);
		return stringBuilder.ToString();
	}

	private void AppendClassProperties(Type type, StringBuilder builder)
	{
		builder.Append('{');
		PropertyInfo[] properties = type.GetProperties();
		foreach (PropertyInfo propertyInfo in properties)
		{
			builder.Append(' ');
			builder.Append(propertyInfo.Name);
			if (propertyInfo.PropertyType.IsClass && propertyInfo.PropertyType != typeof(string))
			{
				AppendClassProperties(propertyInfo.PropertyType, builder);
			}
		}
		builder.Append(" }");
	}
}
