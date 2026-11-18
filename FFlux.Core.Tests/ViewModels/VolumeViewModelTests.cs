using Screenbox.Core.Contexts;
using Screenbox.Core.Services;
using Screenbox.Core.Tests.Helpers;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Tests.ViewModels;

/// <summary>
/// Verifies that the app slider and the Windows system master volume stay in sync without
/// double-attenuating the player's own volume.
/// </summary>
[NotInParallel]
public class VolumeViewModelTests
{
    private sealed class FakeSystemVolumeService : ISystemVolumeService
    {
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public bool Available { get; set; } = true;

        public int SetVolumeCalls { get; private set; }
        public int LastSetVolume { get; private set; }
        public int SetMuteCalls { get; private set; }
        public bool LastSetMute { get; private set; }

        public bool TryGetVolume(out int volume)
        {
            volume = Volume;
            return Available;
        }

        public bool TrySetVolume(int volume)
        {
            SetVolumeCalls++;
            LastSetVolume = volume;
            if (Available) Volume = volume;
            return Available;
        }

        public bool TryGetMute(out bool mute)
        {
            mute = Mute;
            return Available;
        }

        public bool TrySetMute(bool mute)
        {
            SetMuteCalls++;
            LastSetMute = mute;
            if (Available) Mute = mute;
            return Available;
        }
    }

    private static VolumeViewModel Create(
        FakeSystemVolumeService system,
        out PlaybackSettingsContext settings,
        out TestMediaPlayer player)
    {
        TestAppDispatcher.Instance.Clear();
        player = new TestMediaPlayer();
        var playerContext = new PlayerContext(player);
        settings = new PlaybackSettingsContext(new MemorySettingsStorage());
        return new VolumeViewModel(settings, playerContext, system);
    }

    [Test]
    public async Task Constructor_AdoptsSystemVolumeInsteadOfPersistedValue()
    {
        TestAppDispatcher.Instance.Clear();
        var system = new FakeSystemVolumeService { Volume = 42 };
        var settings = new PlaybackSettingsContext(new MemorySettingsStorage()) { Volume = 80 };

        var vm = new VolumeViewModel(
            settings, new PlayerContext(new TestMediaPlayer()), system);

        await Assert.That(vm.Volume).IsEqualTo(42);
        // Adoption must not immediately write our persisted value (80) over the system (42).
        await Assert.That(system.Volume).IsEqualTo(42);
    }

    [Test]
    public async Task SetVolume_WritesSystemMasterVolume()
    {
        var system = new FakeSystemVolumeService { Volume = 40 };
        var vm = Create(system, out _, out _);

        vm.SetVolume(65);

        await Assert.That(system.LastSetVolume).IsEqualTo(65);
        await Assert.That(system.Volume).IsEqualTo(65);
    }

    [Test]
    public async Task SetVolume_AboveHundred_PinsSystemAtHundredAndBoostsPlayer()
    {
        var system = new FakeSystemVolumeService { Volume = 100 };
        var settings = new PlaybackSettingsContext(new MemorySettingsStorage()) { MaxVolume = 200 };
        var player = new TestMediaPlayer();
        var vm = new VolumeViewModel(settings, new PlayerContext(player), system);

        vm.SetVolume(150);
        TestAppDispatcher.Instance.Flush();

        await Assert.That(system.LastSetVolume).IsEqualTo(100);
        await Assert.That(player.Volume).IsEqualTo(1.5);
    }

    [Test]
    public async Task SetVolume_AtFifty_KeepsPlayerAtUnityToAvoidDoubleAttenuation()
    {
        var system = new FakeSystemVolumeService { Volume = 100 };
        var vm = Create(system, out _, out var player);

        vm.SetVolume(50);
        TestAppDispatcher.Instance.Flush();

        await Assert.That(system.LastSetVolume).IsEqualTo(50);
        await Assert.That(player.Volume).IsEqualTo(1.0);
    }

    [Test]
    public async Task MuteToggle_WritesSystemMute()
    {
        var system = new FakeSystemVolumeService { Volume = 100, Mute = false };
        var vm = Create(system, out _, out _);

        vm.HandleMuteToggleKey();

        await Assert.That(vm.IsMute).IsTrue();
        await Assert.That(system.LastSetMute).IsTrue();
    }

    [Test]
    public async Task WithoutSystemEndpoint_FallsBackToPlayerVolume()
    {
        var system = new FakeSystemVolumeService { Available = false };
        var vm = Create(system, out _, out var player);

        vm.SetVolume(35);
        TestAppDispatcher.Instance.Flush();

        await Assert.That(player.Volume).IsEqualTo(0.35);
        // The system write is best-effort and returns false without an endpoint; the app
        // volume still works locally.
        await Assert.That(system.Volume).IsEqualTo(100);
    }
}
