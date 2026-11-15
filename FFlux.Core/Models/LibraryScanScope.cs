using System.Collections.Generic;
using Windows.Storage;
using Windows.Storage.Search;

namespace Screenbox.Core.Models;

/// <summary>
/// Everything one library scan covers: the folders to walk, the queries over those folders, and
/// the storage library that supplies change tracking.
/// </summary>
/// <remarks>
/// A library can span several folders - the ones the user registered with Windows plus the ones
/// added inside the app - and WinRT has no query that merges arbitrary folders, so there is one
/// query per folder.
/// </remarks>
public sealed class LibraryScanScope
{
    public LibraryScanScope(
        StorageLibrary library,
        IReadOnlyList<StorageFolder> roots,
        IReadOnlyList<StorageFileQueryResult> queries)
    {
        Library = library;
        Roots = roots;
        Queries = queries;
    }

    /// <summary>Gets the storage library, used for its change tracker.</summary>
    public StorageLibrary Library { get; }

    /// <summary>Gets the folders that will be walked.</summary>
    public IReadOnlyList<StorageFolder> Roots { get; }

    /// <summary>Gets one query per root.</summary>
    public IReadOnlyList<StorageFileQueryResult> Queries { get; }
}
