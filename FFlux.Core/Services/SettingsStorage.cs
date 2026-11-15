using Windows.Foundation.Collections;

namespace Screenbox.Core.Services;

/// <summary>
/// Implements <see cref="ISettingsStorage"/> backed by the Windows App SDK application
/// data container. An unpackaged app has no package identity, so UWP's
/// Windows.Storage.ApplicationData.Current throws 0x80073D54 here.
/// </summary>
public sealed class SettingsStorage : ISettingsStorage
{
    private readonly IPropertySet _storage;

    public SettingsStorage() : this(
        Microsoft.Windows.Storage.ApplicationData
            .GetForUnpackaged("FFlux", "FFlux")
            .LocalSettings.Values)
    {
    }

    public SettingsStorage(IPropertySet storage)
    {
        _storage = storage;
    }

    public T GetValue<T>(string key, T defaultValue)
    {
        if (_storage.TryGetValue(key, out object? value) && value is T typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }

    public void SetValue<T>(string key, T value)
    {
        _storage[key] = value;
    }
}
