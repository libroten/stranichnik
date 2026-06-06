using System.Collections.Generic;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public sealed record SecretProjectionResult(
    BookmarkTreeSnapshot Snapshot,
    IReadOnlyList<SecretProjectionWarning> Warnings);
