using Stranichnik.Security;

namespace Stranichnik.Storage;

public interface ISecretProfileStore
{
    CryptoProfileRecord? LoadActiveProfile();

    CryptoProfileRecord SaveNewProfile(CryptoProfileRecord profile);

    CryptoProfileRecord UpdateProfile(CryptoProfileRecord profile);
}
