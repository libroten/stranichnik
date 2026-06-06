using System;

namespace Stranichnik.Security;

public sealed class SecretPayloadException : Exception
{
    public SecretPayloadException()
        : this(SecretCryptoFailureReason.InvalidPayload)
    {
    }

    public SecretPayloadException(string message)
        : base(message)
    {
        FailureReason = SecretCryptoFailureReason.InvalidPayload;
    }

    public SecretPayloadException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        FailureReason = SecretCryptoFailureReason.InvalidPayload;
    }

    public SecretPayloadException(SecretCryptoFailureReason failureReason)
        : base("Secret payload operation failed.")
    {
        FailureReason = failureReason;
    }

    public SecretPayloadException(
        SecretCryptoFailureReason failureReason,
        Exception innerException)
        : base("Secret payload operation failed.", innerException)
    {
        FailureReason = failureReason;
    }

    public SecretCryptoFailureReason FailureReason { get; }
}
