namespace Stranichnik.Security;

public sealed record CryptoProfileCreationResult(
    CryptoProfileRecord Profile,
    RuntimeSecretKey DataKey);
