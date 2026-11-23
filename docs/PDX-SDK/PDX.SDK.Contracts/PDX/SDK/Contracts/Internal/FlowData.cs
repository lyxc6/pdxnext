using System;
using System.Collections.Generic;
using System.Text;
using PDX.SDK.Contracts.StandardConstantValues;

namespace PDX.SDK.Contracts.Internal;

public class FlowData
{
	private string _idCache;

	private List<string> _identifiers;

	private static Random random = new Random();

	public string Id => _idCache;

	public string ChildId => string.Format(Defaults.Culture, "{0}[{1}]", Id, GenerateLogId());

	public FlowData()
	{
		_identifiers = new List<string>();
		PushId();
	}

	public FlowData(string id)
	{
		_identifiers = new List<string>();
		PushId(id);
	}

	public FlowData(FlowData data)
	{
		if (data == null)
		{
			_identifiers = new List<string>();
		}
		else
		{
			_identifiers = new List<string>(data._identifiers);
		}
		PushId();
	}

	public FlowData(FlowData data, string id)
	{
		if (data == null)
		{
			_identifiers = new List<string>();
		}
		else
		{
			_identifiers = new List<string>(data._identifiers);
		}
		PushId(id);
	}

	public void PushId()
	{
		PushId(GenerateLogId());
	}

	public void PushId(string id)
	{
		_identifiers.Add(id);
		Rebuild();
	}

	public void PopId()
	{
		if (_identifiers.Count > 0)
		{
			_identifiers.RemoveAt(_identifiers.Count - 1);
		}
		Rebuild();
	}

	private void Rebuild()
	{
		StringBuilder stringBuilder = new StringBuilder("");
		foreach (string identifier in _identifiers)
		{
			stringBuilder.AppendFormat("[{0}]", identifier);
		}
		_idCache = stringBuilder.ToString();
	}

	private string GenerateLogId(int size = 4)
	{
		string text = random.Next().ToString("X", Defaults.Culture);
		if (text.Length >= size)
		{
			return text.Substring(text.Length - size);
		}
		return text.PadLeft(size, '0');
	}
}
