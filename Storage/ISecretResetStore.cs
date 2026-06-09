using System.Collections.Generic;

namespace Stranichnik.Storage;

public interface ISecretResetStore
{
    SecretResetStoreResult ResetMasterPasswordAndPurgeSecrets(string secretGenerationId);

    IReadOnlyList<SecretResetEventRecord> LoadResetEvents();
}

