using System;
using System.Collections.Generic;

namespace Screenbox.Core.Helpers;

/// <summary>
/// A bounded LRU cache for album-art bytes. Kept free of WinRT types so the eviction policy can
/// be unit tested in the plain desktop test host.
/// </summary>
/// <remarks>
/// One play used to decode the same 800 KB cover several times (thumbnail, now-playing, SMTC
/// flyout), often concurrently, before any shared state existed. The byte arrays are shared
/// references: callers must treat them as read-only.
/// </remarks>
public sealed class ArtworkByteCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private long _bytes;

    public ArtworkByteCache(int maxEntries = 48, long maxBytes = 16L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public long Bytes
    {
        get { lock (_gate) return _bytes; }
    }

    public byte[]? TryGet(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry)) return null;
            _lru.Remove(entry.Node);
            _lru.AddLast(entry.Node);
            return entry.Data;
        }
    }

    public void Store(string key, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrEmpty(key) || data.Length == 0 || data.Length > _maxBytes) return;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? existing))
            {
                _bytes -= existing.Data.Length;
                _lru.Remove(existing.Node);
            }

            LinkedListNode<string> node = _lru.AddLast(key);
            _entries[key] = new Entry(data, node);
            _bytes += data.Length;
            Trim();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lru.Clear();
            _bytes = 0;
        }
    }

    private void Trim()
    {
        while (_entries.Count > _maxEntries || (_bytes > _maxBytes && _entries.Count > 0))
        {
            LinkedListNode<string>? oldest = _lru.First;
            if (oldest is null) break;

            _lru.RemoveFirst();
            if (_entries.Remove(oldest.Value, out Entry? removed))
            {
                _bytes -= removed.Data.Length;
            }
        }
    }

    private sealed class Entry(byte[] data, LinkedListNode<string> node)
    {
        public byte[] Data { get; } = data;
        public LinkedListNode<string> Node { get; } = node;
    }
}
