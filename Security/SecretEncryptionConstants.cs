namespace Stranichnik.Security;

public static class SecretEncryptionConstants
{
    public const int CurrentProfileVersion = 1;
    public const int CurrentPayloadFormatVersion = 1;

    public const string KdfName = "PBKDF2";
    public const string KdfHashAlgorithm = "SHA256";
    public const int DefaultPbkdf2Iterations = 600000;
    public const int KdfSaltLengthBytes = 32;
    public const int KekLengthBytes = 32;
    public const int DataKeyLengthBytes = 32;
    public const int AesGcmNonceLengthBytes = 12;
    public const int AesGcmTagLengthBytes = 16;

    public const string DataKeyAlgorithm = "AES-256-GCM-DEK";
    public const string EncryptionAlgorithm = "AES-256-GCM";
    public const string PayloadFormat = "stranichnik-secret-json-v1";
}
