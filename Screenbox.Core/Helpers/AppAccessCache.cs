using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Windows.Storage.AccessCache (MRU / FutureAccessList) is backed by package-local
/// state. In an unpackaged WinUI 3 process every access throws E_INVALIDARG
/// (0x80070057) or 0x80073D54, and an exception escaping an async void handler is
/// turned by CoreMessaging into a fatal stowed exception (0xC000027B).
///
/// All access therefore goes through these best-effort wrappers. Until the app is
/// packaged again, "recent files" and persisted folder tokens are simply disabled:
/// the UI stays empty instead of crashing on startup.
/// </summary>
public static class AppAccessCache
{
    private static bool? _available;

    /// <summary>
    /// Probed once. Reading Entries is the cheapest operation that reliably fails
    /// without package identity; the result is cached so startup only pays for one
    /// (caught) COM exception.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            if (_available.HasValue) return _available.Value;
            try
            {
                _ = StorageApplicationPermissions.MostRecentlyUsedList.Entries;
                _available = true;
            }
            catch (Exception)
            {
                _available = false;
            }

            return _available.Value;
        }
    }

    public static IReadOnlyList<AccessListEntry> GetRecentEntries()
    {
        if (!IsAvailable) return Array.Empty<AccessListEntry>();
        try
        {
            return StorageApplicationPermissions.MostRecentlyUsedList.Entries.ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<AccessListEntry>();
        }
    }

    public static void AddToRecent(IStorageItem item, string metadata)
    {
        if (!IsAvailable) return;
        try
        {
            StorageApplicationPermissions.MostRecentlyUsedList.Add(item, metadata);
        }
        catch (Exception)
        {
            // Recent-files tracking is best-effort.
        }
    }

    public static void RemoveFromRecent(string token)
    {
        if (!IsAvailable || string.IsNullOrEmpty(token)) return;
        try
        {
            StorageApplicationPermissions.MostRecentlyUsedList.Remove(token);
        }
        catch (Exception)
        {
            // Stale token; nothing to clean up.
        }
    }

    public static void ClearRecent()
    {
        if (!IsAvailable) return;
        try
        {
            StorageApplicationPermissions.MostRecentlyUsedList.Clear();
        }
        catch (Exception)
        {
            // Nothing to clear without package identity.
        }
    }

    public static async Task<StorageFile?> GetRecentFileAsync(string token)
    {
        if (!IsAvailable || string.IsNullOrEmpty(token)) return null;
        try
        {
            return await StorageApplicationPermissions.MostRecentlyUsedList
                .GetFileAsync(token, AccessCacheOptions.SuppressAccessTimeUpdate);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? AddFolderToFutureAccess(StorageFolder folder, string metadata)
    {
        if (!IsAvailable) return null;
        try
        {
            return StorageApplicationPermissions.FutureAccessList.Add(folder, metadata);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void RemoveFromFutureAccess(string token)
    {
        if (!IsAvailable || string.IsNullOrEmpty(token)) return;
        try
        {
            if (StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
            {
                StorageApplicationPermissions.FutureAccessList.Remove(token);
            }
        }
        catch (Exception)
        {
            // No persisted access list in an unpackaged process.
        }
    }

    public static async Task<StorageFolder?> GetFolderFromFutureAccessAsync(string token)
    {
        if (!IsAvailable || string.IsNullOrEmpty(token)) return null;
        try
        {
            if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(token)) return null;
            return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
