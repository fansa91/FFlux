using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Tests;

public class ArtworkByteCacheTests
{
    [Test]
    public async Task Store_ThenTryGet_ReturnsTheSameArray()
    {
        var cache = new ArtworkByteCache();
        byte[] data = new byte[128];

        cache.Store("cover.jpg", data);

        await Assert.That(ReferenceEquals(cache.TryGet("cover.jpg"), data)).IsTrue();
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.Bytes).IsEqualTo(128L);
    }

    [Test]
    public async Task Store_SameKeyTwice_DoesNotDoubleCountBytes()
    {
        var cache = new ArtworkByteCache();

        cache.Store("cover.jpg", new byte[100]);
        cache.Store("cover.jpg", new byte[300]);

        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.Bytes).IsEqualTo(300L);
        await Assert.That(cache.TryGet("cover.jpg")!.Length).IsEqualTo(300);
    }

    [Test]
    public async Task Store_BeyondEntryLimit_EvictsLeastRecentlyUsed()
    {
        var cache = new ArtworkByteCache(maxEntries: 3, maxBytes: 10000);

        cache.Store("a", new byte[100]);
        cache.Store("b", new byte[100]);
        cache.Store("c", new byte[100]);
        _ = cache.TryGet("a"); // refresh a so b becomes the oldest
        cache.Store("d", new byte[100]);

        await Assert.That(cache.Count).IsEqualTo(3);
        await Assert.That(cache.TryGet("b")).IsNull();
        await Assert.That(cache.TryGet("a")).IsNotNull();
        await Assert.That(cache.TryGet("c")).IsNotNull();
        await Assert.That(cache.TryGet("d")).IsNotNull();
    }

    [Test]
    public async Task Store_BeyondByteLimit_KeepsTheBudget()
    {
        var cache = new ArtworkByteCache(maxEntries: 10, maxBytes: 250);

        cache.Store("a", new byte[100]);
        cache.Store("b", new byte[100]);
        cache.Store("c", new byte[100]);

        await Assert.That(cache.Bytes).IsEqualTo(200L);
        await Assert.That(cache.Count).IsEqualTo(2);
        await Assert.That(cache.TryGet("a")).IsNull();
    }

    [Test]
    public async Task Store_OversizedEntry_IsRejected()
    {
        var cache = new ArtworkByteCache(maxEntries: 4, maxBytes: 200);

        cache.Store("huge", new byte[1000]);

        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.Bytes).IsEqualTo(0L);
    }

    [Test]
    public async Task Lookup_IsCaseInsensitive_AndClearResetsEverything()
    {
        var cache = new ArtworkByteCache();
        cache.Store(@"C:\Music\Cover.jpg", new byte[64]);

        await Assert.That(cache.TryGet(@"c:\music\cover.JPG")).IsNotNull();

        cache.Clear();
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.Bytes).IsEqualTo(0L);
        await Assert.That(cache.TryGet("C:\\Music\\Cover.jpg")).IsNull();
    }

    [Test]
    public async Task ConcurrentAccess_NeverExceedsTheLimits()
    {
        var cache = new ArtworkByteCache(maxEntries: 8, maxBytes: 4000);
        var errors = new List<Exception>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    cache.Store($"cover-{worker}-{i % 12}.jpg", new byte[100]);
                    _ = cache.TryGet($"cover-{worker}-{i % 12}.jpg");
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex);
            }
        })));

        await Assert.That(errors.Count).IsEqualTo(0);
        await Assert.That(cache.Count <= 8).IsTrue();
        await Assert.That(cache.Bytes <= 4000).IsTrue();
    }
}
