using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Screenbox.Core.Enums;

namespace Screenbox.Core.Services;

public sealed partial class DatabaseService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetLibraryRootsAsync(MediaPlaybackType mediaType)
    {
        await EnsureInitializedAsync();
        using var connection = CreateConnection();

        var paths = new List<string>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT path FROM library_roots WHERE media_type = @mt ORDER BY id;";
        cmd.Parameters.AddWithValue("@mt", (int)mediaType);
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    /// <inheritdoc/>
    public async Task<bool> AddLibraryRootAsync(MediaPlaybackType mediaType, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO library_roots (path, media_type) VALUES (@path, @mt);";
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@mt", (int)mediaType);
        // INSERT OR IGNORE reports no row when the folder was already there.
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveLibraryRootAsync(MediaPlaybackType mediaType, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM library_roots WHERE path = @path AND media_type = @mt;";
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@mt", (int)mediaType);
        return cmd.ExecuteNonQuery() > 0;
    }
}
