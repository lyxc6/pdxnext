namespace PDX.SDK.Internal.Platypatch.Pgp.Enums;

public enum TagType
{
	Reserved = 0,
	PublicKeyEncryptedSessionKey = 1,
	Signature = 2,
	SymmetricKeyEncryptedSessionKey = 3,
	OnePassSignature = 4,
	SecretKey = 5,
	PublicKey = 6,
	SecretSubkey = 7,
	CompressedData = 8,
	SymmetricallyEncryptedData = 9,
	MarkerPacket = 10,
	LiteralData = 11,
	Trust = 12,
	UserID = 13,
	PublicSubkey = 14,
	UserAttribute = 17,
	SymEncryptedIntegrityProtectedData = 18,
	ModificationDetectionCode = 19
}
