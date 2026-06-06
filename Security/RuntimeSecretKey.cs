using System;
using System.Security.Cryptography;

namespace Stranichnik.Security;

public sealed class RuntimeSecretKey : IDisposable
{
    private readonly byte[] _key;
    private bool _isDisposed;

    public RuntimeSecretKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != SecretEncryptionConstants.DataKeyLengthBytes)
            throw new ArgumentException("Secret key has an unsupported length.", nameof(key));

        _key = key.ToArray();
    }

    internal ReadOnlySpan<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            return _key;
        }
    }

    public bool IsDisposed => _isDisposed;

    public void Dispose()
    {
        if (_isDisposed)
            return;

        CryptographicOperations.ZeroMemory(_key);
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
