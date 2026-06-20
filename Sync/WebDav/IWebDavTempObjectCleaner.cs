using System.Threading;
using System.Threading.Tasks;

namespace Stranichnik.Sync.WebDav;

public interface IWebDavTempObjectCleaner
{
    Task CleanupStaleTempObjectsAsync(CancellationToken cancellationToken);
}
