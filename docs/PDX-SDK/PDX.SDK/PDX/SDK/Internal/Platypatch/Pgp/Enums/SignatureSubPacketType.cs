namespace PDX.SDK.Internal.Platypatch.Pgp.Enums;

public enum SignatureSubPacketType
{
	SignatureCreationTime = 2,
	SignatureExpirationTime = 3,
	ExportableCertification = 4,
	TrustSignature = 5,
	RegularExpression = 6,
	Revocable = 7,
	KeyExpirationTime = 9,
	PreferredSymmetricAlgorithms = 11,
	RevocationKey = 12,
	Issuer = 16,
	NotationData = 20,
	PreferredHashAlgorithms = 21,
	PreferredCompressionAlgorithms = 22,
	KeyServerPreferences = 23,
	PreferredKeyServer = 24,
	PrimaryUserID = 25,
	PolicyURI = 26,
	KeyFlags = 27,
	SignerUserID = 28,
	ReasonForRevocation = 29,
	Features = 30,
	SignatureTarget = 31,
	EmbeddedSignature = 32
}
