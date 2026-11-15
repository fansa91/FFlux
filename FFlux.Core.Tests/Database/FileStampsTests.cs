using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Core.Tests.Helpers;

namespace Screenbox.Core.Tests.Database;

public sealed class FileStampsTests
{
    [Test]
    public async Task InitializeAsync_CreatesTheFileStampsTable()
    {
        using var fixture = new TestDirectoryFixture();
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance)
        {
            DbFolderPath = fixture.DirectoryPath,
        };

        await db.InitializeAsync();

        string dbPath = System.IO.Path.Combine(fixture.DirectoryPath, AppDataMigration.CurrentDatabaseFileName);
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='media_file_stamps';";
        long count = (long)(cmd.ExecuteScalar() ?? 0L);
        await Assert.That(count).IsEqualTo(1L);
    }

    [Test]
    public async Task SaveFileStampsAsync_UpdatesChanged_RemovesMissing_AndIsolatesMediaTypes()
    {
        using var fixture = new TestDirectoryFixture();
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance)
        {
            DbFolderPath = fixture.DirectoryPath,
        };
        await db.InitializeAsync();

        var initial = new Dictionary<string, FileStampDto>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Music\a.mp3"] = new() { Path = @"C:\Music\a.mp3", Size = 100, ModifiedTicks = 111 },
            [@"C:\Music\b.mp3"] = new() { Path = @"C:\Music\b.mp3", Size = 200, ModifiedTicks = 222 },
        };
        await db.SaveFileStampsAsync(MediaPlaybackType.Music, initial);

        IReadOnlyDictionary<string, FileStampDto> loaded = await db.LoadFileStampsAsync(MediaPlaybackType.Music);
        await Assert.That(loaded.Count).IsEqualTo(2);
        await Assert.That(loaded[@"c:\music\A.mp3"].Size).IsEqualTo(100L);

        var next = new Dictionary<string, FileStampDto>(StringComparer.OrdinalIgnoreCase)
        {
            // a was rewritten on disk
            [@"C:\Music\a.mp3"] = new() { Path = @"C:\Music\a.mp3", Size = 150, ModifiedTicks = 333 },
            // b was deleted, c is new
            [@"C:\Music\c.mp3"] = new() { Path = @"C:\Music\c.mp3", Size = 300, ModifiedTicks = 444 },
        };
        await db.SaveFileStampsAsync(MediaPlaybackType.Music, next);

        loaded = await db.LoadFileStampsAsync(MediaPlaybackType.Music);
        await Assert.That(loaded.Count).IsEqualTo(2);
        await Assert.That(loaded[@"C:\Music\a.mp3"].Size).IsEqualTo(150L);
        await Assert.That(loaded[@"C:\Music\a.mp3"].ModifiedTicks).IsEqualTo(333L);
        await Assert.That(loaded.ContainsKey(@"C:\Music\b.mp3")).IsFalse();
        await Assert.That(loaded.ContainsKey(@"C:\Music\c.mp3")).IsTrue();

        IReadOnlyDictionary<string, FileStampDto> videos = await db.LoadFileStampsAsync(MediaPlaybackType.Video);
        await Assert.That(videos.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SaveFileStampsAsync_WithNoChanges_KeepsTheSnapshot()
    {
        using var fixture = new TestDirectoryFixture();
        var db = new DatabaseService(NullLogger<DatabaseService>.Instance)
        {
            DbFolderPath = fixture.DirectoryPath,
        };
        await db.InitializeAsync();

        var stamps = new Dictionary<string, FileStampDto>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new() { Path = "a", Size = 1, ModifiedTicks = 2 },
        };

        await db.SaveFileStampsAsync(MediaPlaybackType.Music, stamps);
        await db.SaveFileStampsAsync(MediaPlaybackType.Music, stamps);

        IReadOnlyDictionary<string, FileStampDto> loaded = await db.LoadFileStampsAsync(MediaPlaybackType.Music);
        await Assert.That(loaded.Count).IsEqualTo(1);
        await Assert.That(loaded["a"].ModifiedTicks).IsEqualTo(2L);
    }
}
