using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Screenbox.Core.Models;
using Screenbox.Core.Models.Serialization;

namespace Screenbox.Core.Services;

/// <summary>
/// Owns the saved network stream list and keeps it in sync with a JSON file on disk.
/// </summary>
public interface IStreamManagerService
{
    /// <summary>Gets the live collection shown by the streams page. Newest entries come first.</summary>
    ObservableCollection<StreamItem> Items { get; }

    /// <summary>Gets whether <see cref="LoadAsync"/> has completed at least once.</summary>
    bool IsLoaded { get; }

    /// <summary>Reads the JSON file. Safe to call more than once.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Flushes the current list to disk, cancelling any pending debounced write.</summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds an entry at the top of the list and schedules a save.</summary>
    StreamItem Add(StreamItem item);

    /// <summary>Removes an entry and schedules a save.</summary>
    void Remove(StreamItem item);

    /// <summary>Replaces the editable fields of an existing entry and schedules a save.</summary>
    void Update(StreamItem target, StreamItem source);

    /// <summary>Renames an entry in place and schedules a save.</summary>
    void Rename(StreamItem item, string newName);
}

/// <summary>
/// Default <see cref="IStreamManagerService"/> implementation, backed by
/// <c>%LOCALAPPDATA%\FFlux\streams.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every mutation schedules a debounced write instead of saving inline, so a rename typed into a
/// dialog or a burst of property changes collapses into one file write. <see cref="SaveAsync"/>
/// forces an immediate flush and is what the app calls on shutdown.
/// </para>
/// <para>
/// The file is replaced atomically (write to <c>.tmp</c>, then move over the target), so a crash
/// mid-write cannot leave a truncated library behind.
/// </para>
/// </remarks>
public sealed class StreamManagerService : IStreamManagerService, IDisposable
{
    private static readonly TimeSpan s_saveDebounce = TimeSpan.FromMilliseconds(400);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        // Keep CJK names readable in the file instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Native AOT disables reflection based serialization, so the resolver has to come from
        // the source generated context. Without it every write throws at runtime.
        TypeInfoResolver = StreamJsonContext.Default,
    };

    private readonly SemaphoreSlim _ioLock = new(1, 1);

    /// <summary>
    /// Guards <see cref="Items"/> against being enumerated off the UI thread. The debounce timer
    /// fires on a thread pool thread, so without this a write could race the UI mutating the list.
    /// </summary>
    private readonly object _itemsLock = new();

    private readonly Timer _saveTimer;
    private bool _isLoaded;
    private bool _isDisposed;

    /// <summary>
    /// Gets or sets the JSON file location. Overridable so tests can target a temporary folder;
    /// the app keeps the default.
    /// </summary>
    public string FilePath { get; set; } = DefaultFilePath;

    /// <summary>
    /// Gets the default library path: <c>%LOCALAPPDATA%\FFlux\streams.json</c>.
    /// </summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFlux",
        "streams.json");

    /// <summary>
    /// Gets the serializer options used for the library file.
    /// </summary>
    /// <remarks>
    /// The resolver comes from a source generated context on purpose: the app publishes as
    /// Native AOT, where reflection based serialization is switched off at runtime.
    /// </remarks>
    public static JsonSerializerOptions SerializerOptions => s_jsonOptions;

    /// <inheritdoc/>
    public ObservableCollection<StreamItem> Items { get; } = [];

    /// <inheritdoc/>
    public bool IsLoaded => _isLoaded;

    public StreamManagerService()
    {
        _saveTimer = new Timer(_ => _ = SaveAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc/>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded) return;

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isLoaded) return;

            string? json = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
            List<StreamItem> loaded = Deserialize(json);

            lock (_itemsLock)
            {
                Items.Clear();
                foreach (StreamItem item in loaded)
                {
                    Attach(item);
                    Items.Add(item);
                }
            }

            _isLoaded = true;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return;

        // A forced save supersedes whatever the debounce timer was waiting to do.
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);

        // Snapshot synchronously, before the first await: the collection belongs to the UI thread
        // and must not be enumerated from the pool thread the debounce timer runs on.
        List<StreamItem> snapshot;
        lock (_itemsLock)
        {
            snapshot = Items.ToList();
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
        }
        catch (Exception)
        {
            return;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFileAsync(json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Persistence is best effort: a read-only profile or a locked file must not break
            // playback or the UI. The in-memory list stays authoritative for the session.
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <inheritdoc/>
    public StreamItem Add(StreamItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Attach(item);
        lock (_itemsLock)
        {
            Items.Insert(0, item);
        }

        ScheduleSave();
        return item;
    }

    /// <inheritdoc/>
    public void Remove(StreamItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Detach(item);
        lock (_itemsLock)
        {
            Items.Remove(item);
        }

        ScheduleSave();
    }

    /// <inheritdoc/>
    public void Update(StreamItem target, StreamItem source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        target.CopyFrom(source);
        ScheduleSave();
    }

    /// <inheritdoc/>
    public void Rename(StreamItem item, string newName)
    {
        ArgumentNullException.ThrowIfNull(item);
        string trimmed = newName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed == item.Name) return;

        item.Name = trimmed;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_isDisposed) return;
        _saveTimer.Change(s_saveDebounce, Timeout.InfiniteTimeSpan);
    }

    private void Attach(StreamItem item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void Detach(StreamItem item)
    {
        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // BadgeText is derived from FormatType, which raises its own change already.
        if (e.PropertyName == nameof(StreamItem.BadgeText)) return;
        ScheduleSave();
    }

    private async Task<string?> ReadFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<StreamItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            List<StreamItem>? items = JsonSerializer.Deserialize<List<StreamItem>>(json, s_jsonOptions);
            if (items is null) return [];

            List<StreamItem> valid = items
                .Where(i => i is not null)
                .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                .ToList();

            foreach (StreamItem item in valid)
            {
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = item.Url;
            }

            return valid;
        }
        catch (JsonException)
        {
            // Keep the unreadable file for inspection instead of overwriting it with an empty
            // library on the next save.
            TryBackupCorruptFile();
            return [];
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            string backup = FilePath + ".corrupt";
            File.Copy(FilePath, backup, overwrite: true);
        }
        catch (Exception)
        {
            // Nothing useful to do if the file cannot be copied.
        }
    }

    private async Task WriteFileAsync(string json, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>
    /// Stops the debounce timer. Any pending change is dropped: callers flush with
    /// <see cref="SaveAsync"/> before disposing.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _saveTimer.Dispose();
        _ioLock.Dispose();
    }
}
