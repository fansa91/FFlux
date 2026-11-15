using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Screenbox.Core.Services;

public sealed partial class DatabaseService
{
    /// <summary>
    /// Rows kept in <c>recent_items</c>. The home page only ever asks for a few dozen; the rest is
    /// headroom for the case where that grows, and it keeps the table from growing without bound.
    /// </summary>
    private const int MaxStoredRecentItems = 200;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetRecentItemPathsAsync(int limit)
    {
        if (limit <= 0) return Array.Empty<string>();

        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        // rowid breaks ties: several files queued in the same millisecond still keep a stable order
        // instead of shuffling between reads.
        cmd.CommandText = """
            SELECT path FROM recent_items
            ORDER BY played_at DESC, rowid DESC
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        var paths = new List<string>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }

        return paths;
    }

    /// <inheritdoc/>
    public async Task TouchRecentItemAsync(string path, long playedAtUnixMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (playedAtUnixMilliseconds <= 0) return;

        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO recent_items (path, played_at) VALUES (@path, @playedAt)
            ON CONFLICT(path) DO UPDATE SET played_at = excluded.played_at;
            """;
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@playedAt", playedAtUnixMilliseconds);
        cmd.ExecuteNonQuery();

        // Trim the tail in the same round-trip so the table cannot grow forever.
        using var trim = connection.CreateCommand();
        trim.CommandText = """
            DELETE FROM recent_items
            WHERE path NOT IN (
                SELECT path FROM recent_items ORDER BY played_at DESC, rowid DESC LIMIT @keep
            );
            """;
        trim.Parameters.AddWithValue("@keep", MaxStoredRecentItems);
        trim.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public async Task RemoveRecentItemAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM recent_items WHERE path = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public async Task ClearRecentItemsAsync()
    {
        await EnsureInitializedAsync();
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM recent_items;";
        cmd.ExecuteNonQuery();
    }
}
