using Microsoft.Extensions.Logging.Abstractions;
using Screenbox.Core.Services;
using Screenbox.Core.Tests.Helpers;

namespace Screenbox.Core.Tests.Database;

/// <summary>
/// Playback history is stored in the app database: the Windows MRU list that used to back it needs
/// package identity and always throws in this build.
/// </summary>
public sealed class RecentItemsTests
{
    private static async Task<DatabaseService> CreateServiceAsync(TestDirectoryFixture fixture)
    {
        var service = new DatabaseService(NullLogger<DatabaseService>.Instance)
        {
            DbFolderPath = fixture.DirectoryPath,
        };
        await service.InitializeAsync();
        return service;
    }

    [Test]
    public async Task GetRecentItemPathsAsync_ReturnsNewestFirst()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);

        await db.TouchRecentItemAsync(@"C:\Media\oldest.mp3", 1_000);
        await db.TouchRecentItemAsync(@"C:\Media\middle.mp3", 2_000);
        await db.TouchRecentItemAsync(@"C:\Media\newest.mp3", 3_000);

        IReadOnlyList<string> paths = await db.GetRecentItemPathsAsync(10);

        await Assert.That(paths).Count().IsEqualTo(3);
        await Assert.That(paths[0]).IsEqualTo(@"C:\Media\newest.mp3");
        await Assert.That(paths[1]).IsEqualTo(@"C:\Media\middle.mp3");
        await Assert.That(paths[2]).IsEqualTo(@"C:\Media\oldest.mp3");
    }

    [Test]
    public async Task TouchRecentItemAsync_SamePathTwice_KeepsOneRowAndMovesItToTheTop()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);

        await db.TouchRecentItemAsync(@"C:\Media\a.mp3", 1_000);
        await db.TouchRecentItemAsync(@"C:\Media\b.mp3", 2_000);
        await db.TouchRecentItemAsync(@"C:\Media\a.mp3", 3_000);

        IReadOnlyList<string> paths = await db.GetRecentItemPathsAsync(10);

        await Assert.That(paths).Count().IsEqualTo(2);
        await Assert.That(paths[0]).IsEqualTo(@"C:\Media\a.mp3");
        await Assert.That(paths[1]).IsEqualTo(@"C:\Media\b.mp3");
    }

    [Test]
    public async Task TouchRecentItemAsync_SameTimestamp_KeepsAStableOrder()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);

        await db.TouchRecentItemAsync(@"C:\Media\first.mp3", 5_000);
        await db.TouchRecentItemAsync(@"C:\Media\second.mp3", 5_000);

        IReadOnlyList<string> paths = await db.GetRecentItemPathsAsync(10);

        // The rowid tiebreak keeps the later insert on top instead of letting SQLite pick.
        await Assert.That(paths[0]).IsEqualTo(@"C:\Media\second.mp3");
        await Assert.That(paths[1]).IsEqualTo(@"C:\Media\first.mp3");
    }

    [Test]
    public async Task GetRecentItemPathsAsync_HonoursLimit()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);

        for (int i = 0; i < 5; i++)
        {
            await db.TouchRecentItemAsync($@"C:\Media\{i}.mp3", 1_000 + i);
        }

        IReadOnlyList<string> paths = await db.GetRecentItemPathsAsync(2);

        await Assert.That(paths).Count().IsEqualTo(2);
        await Assert.That(paths[0]).IsEqualTo(@"C:\Media\4.mp3");
        await Assert.That(paths[1]).IsEqualTo(@"C:\Media\3.mp3");
    }

    [Test]
    public async Task GetRecentItemPathsAsync_NonPositiveLimit_ReturnsNothing()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);
        await db.TouchRecentItemAsync(@"C:\Media\a.mp3", 1_000);

        await Assert.That(await db.GetRecentItemPathsAsync(0)).IsEmpty();
    }

    [Test]
    public async Task TouchRecentItemAsync_IgnoresEmptyPathAndNonPositiveTimestamp()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);

        await db.TouchRecentItemAsync("", 1_000);
        await db.TouchRecentItemAsync("   ", 1_000);
        await db.TouchRecentItemAsync(@"C:\Media\a.mp3", 0);

        await Assert.That(await db.GetRecentItemPathsAsync(10)).IsEmpty();
    }

    [Test]
    public async Task RemoveRecentItemAsync_DeletesOnlyThatPath()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);
        await db.TouchRecentItemAsync(@"C:\Media\a.mp3", 1_000);
        await db.TouchRecentItemAsync(@"C:\Media\b.mp3", 2_000);

        await db.RemoveRecentItemAsync(@"C:\Media\a.mp3");

        IReadOnlyList<string> paths = await db.GetRecentItemPathsAsync(10);
        await Assert.That(paths).Count().IsEqualTo(1);
        await Assert.That(paths[0]).IsEqualTo(@"C:\Media\b.mp3");
    }

    [Test]
    public async Task ClearRecentItemsAsync_EmptiesTheHistory()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);
        await db.TouchRecentItemAsync(@"C:\Media\a.mp3", 1_000);
        await db.TouchRecentItemAsync(@"C:\Media\b.mp3", 2_000);

        await db.ClearRecentItemsAsync();

        await Assert.That(await db.GetRecentItemPathsAsync(10)).IsEmpty();
    }

    [Test]
    public async Task InitializeAsync_KeepsExistingHistory()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);
        await db.TouchRecentItemAsync(@"C:\Media\a.mp3", 1_000);

        // A second service over the same file simulates the next app launch: playback history is
        // user data and must survive schema initialisation.
        DatabaseService reopened = await CreateServiceAsync(fixture);

        IReadOnlyList<string> paths = await reopened.GetRecentItemPathsAsync(10);
        await Assert.That(paths).Count().IsEqualTo(1);
        await Assert.That(paths[0]).IsEqualTo(@"C:\Media\a.mp3");
    }

    [Test]
    public async Task TouchRecentItemAsync_KeepsTheTableBounded()
    {
        using var fixture = new TestDirectoryFixture();
        DatabaseService db = await CreateServiceAsync(fixture);

        // The cap is internal (200); writing well past it must not make reads grow without bound.
        for (int i = 0; i < 260; i++)
        {
            await db.TouchRecentItemAsync($@"C:\Media\{i}.mp3", 1_000 + i);
        }

        IReadOnlyList<string> paths = await db.GetRecentItemPathsAsync(1_000);
        await Assert.That(paths.Count <= 200).IsTrue();
        await Assert.That(paths[0]).IsEqualTo(@"C:\Media\259.mp3");
    }
}
