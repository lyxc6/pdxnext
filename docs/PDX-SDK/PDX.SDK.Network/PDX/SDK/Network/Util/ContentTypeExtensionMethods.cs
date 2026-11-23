using System.Collections.Generic;
using PDX.SDK.Network.Enums;

namespace PDX.SDK.Network.Util;

public static class ContentTypeExtensionMethods
{
	private static Dictionary<ContentType, string> ContentTypeStrings = new Dictionary<ContentType, string>
	{
		{
			ContentType.Text,
			"text/plain"
		},
		{
			ContentType.HTML,
			"text/html"
		},
		{
			ContentType.JPEG,
			"image/jpeg"
		},
		{
			ContentType.PNG,
			"image/png"
		},
		{
			ContentType.MPEG,
			"audio/mpeg"
		},
		{
			ContentType.OGG,
			"audio/ogg"
		},
		{
			ContentType.Audio,
			"audio/*"
		},
		{
			ContentType.MP4,
			"video/mp4"
		},
		{
			ContentType.Application,
			"application/*"
		},
		{
			ContentType.JSON,
			"application/json"
		},
		{
			ContentType.Javascript,
			"application/javascript"
		},
		{
			ContentType.ECMAScript,
			"application/ecmascript"
		},
		{
			ContentType.OctetStream,
			"application/octet-stream"
		},
		{
			ContentType.Zip,
			"application/zip"
		},
		{
			ContentType.Undefined,
			""
		}
	};

	public static string ToContentTypeString(this ContentType type)
	{
		if (ContentTypeStrings.ContainsKey(type))
		{
			return ContentTypeStrings[type];
		}
		return "";
	}

	public static ContentType GetContentTypeEnum(string type)
	{
		foreach (KeyValuePair<ContentType, string> contentTypeString in ContentTypeStrings)
		{
			if (type == contentTypeString.Value)
			{
				return contentTypeString.Key;
			}
		}
		return ContentType.Undefined;
	}
}
