using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;

namespace Screenbox.Core.Services;

public sealed partial class DatabaseService
{
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, FileStampDto>> LoadFileStampsAsync(MediaPlaybackType mediaType)
    {
        await EnsureInitializedAsync();
        using var connection = CreateConnection();

        var result = new Dictionary<string, FileStampDto>(StringComparer.OrdinalIgnoreCase);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT path, file_size, modified_ticks FROM media_file_stamps WHERE media_type = @mt;";
        cmd.Parameters.AddWithValue("@mt", (int)mediaType);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string path = reader.GetString(0);
            result[path] = new FileStampDto
            {
                Path = path,
                Size = reader.GetInt64(1),
                ModifiedTicks = reader.GetInt64(2)
            };
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task SaveFileStampsAsync(MediaPlaybackType mediaType, IReadOnlyDictionary<string, FileStampDto> stamps)
    {
        await EnsureInitializedAsync();
        using var connection = CreateConnection();

        var existing = new Dictionary<string, (long Size, long Modified)>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT path, file_size, modified_ticks FROM media_file_stamps WHERE media_type = @mt;";
            cmd.Parameters.AddWithValue("@mt", (int)mediaType);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                existing[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
            }
        }

        // Differential write: a large library that did not change performs zero statements, so a
        // quick validity scan costs no database churn and does not grow the WAL.
        using var transaction = connection.BeginTransaction();
        int writes = 0;

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT OR REPLACE INTO media_file_stamps (path, media_type, file_size, modified_ticks)
                VALUES (@path, @mt, @size, @modified);
                """;
            var pPath = cmd.Parameters.Add("@path", SqliteType.Text);
            cmd.Parameters.AddWithValue("@mt", (int)mediaType);
            var pSize = cmd.Parameters.Add("@size", SqliteType.Integer);
            var pModified = cmd.Parameters.Add("@modified", SqliteType.Integer);

            foreach (KeyValuePair<string, FileStampDto> pair in stamps)
            {
                if (existing.TryGetValue(pair.Key, out (long Size, long Modified) old)
                    && old.Size == pair.Value.Size
                    && old.Modified == pair.Value.ModifiedTicks)
                {
                    continue;
                }

                pPath.Value = pair.Key;
                pSize.Value = pair.Value.Size;
                pModified.Value = pair.Value.ModifiedTicks;
                cmd.ExecuteNonQuery();
                writes++;
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM media_file_stamps WHERE media_type = @mt AND path = @path;";
            cmd.Parameters.AddWithValue("@mt", (int)mediaType);
            var pPath = cmd.Parameters.Add("@path", SqliteType.Text);
            foreach (string path in existing.Keys)
            {
                if (stamps.ContainsKey(path)) continue;
                pPath.Value = path;
                cmd.ExecuteNonQuery();
                writes++;
            }
        }

        transaction.Commit();
        if (writes > 0) ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(PASSIVE);");
    }
}
