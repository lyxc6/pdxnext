namespace PDX.SDK.Internal.Platypatch.Pgp.Enums;

public enum SignatureType
{
	SignatureOfBinaryDocument = 0,
	SignatureOfCanonicalTextDocument = 1,
	StandaloneSignature = 2,
	GenericCertificationOfUserIdAndPublicKeyPacket = 16,
	PersonaCertificationOfUserIdAndPublicKeyPacket = 17,
	CasualCertificationOfUserIdAndPublicKeyPacket = 18,
	PositiveCertificationOfUserIdAndPublicKeyPacket = 19,
	SubkeyBindingSignature = 24,
	PrimaryKeyBindingSignature = 25,
	SignatureDirectlyOnKey = 31,
	KeyRevocationSignature = 32,
	SubkeyRevocationSignature = 40,
	CertificationRevocationSignature = 48,
	ThirdPartConfirmationSignature = 80
}
