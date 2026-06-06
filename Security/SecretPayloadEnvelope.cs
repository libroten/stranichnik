namespace Stranichnik.Security;

internal sealed record SecretPayloadEnvelope(
    int Version,
    string Algorithm,
    string Tag,
    string Ciphertext);
