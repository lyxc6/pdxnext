using System;
using System.Collections.Generic;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;

namespace PDX.SDK.Internal.Platypatch.Pgp;

public class KeyRing
{
	private readonly IMessageValidator _messageValidator;

	private Dictionary<string, PgpKeyMessage> _keys = new Dictionary<string, PgpKeyMessage>();

	public PgpKeyMessage this[string key]
	{
		get
		{
			if (!_keys.ContainsKey(key))
			{
				return null;
			}
			return _keys[key];
		}
	}

	public KeyRing(IMessageValidator messageValidator)
	{
		_messageValidator = messageValidator;
	}

	public void Add(PgpKeyMessage message, bool isRootKey)
	{
		if (!isRootKey)
		{
			ValidateKey(message);
		}
		if (message != null)
		{
			_keys[message.KeyId] = message;
		}
	}

	private void ValidateKey(PgpKeyMessage message)
	{
		if (!_messageValidator.IsValid(message, this))
		{
			throw new Exception("Key " + message.KeyId + " is not valid! Not added to keyring");
		}
	}
}
