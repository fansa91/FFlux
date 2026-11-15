using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;
using Screenbox.Core.Services;

namespace Screenbox.Core.Tests.Services;

public class StreamManagerServiceTests
{
    private static string CreateTempFilePath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "fflux-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "streams.json");
    }

    [Test]
    public async Task Add_ShouldInsertAtTopAndMarkLoadedState()
    {
        using var service = new StreamManagerService { FilePath = CreateTempFilePath() };

        service.Add(new StreamItem { Name = "First", Url = "https://example.com/1.m3u8" });
        service.Add(new StreamItem { Name = "Second", Url = "https://example.com/2.m3u8" });

        await Assert.That(service.Items.Count).IsEqualTo(2);
        await Assert.That(service.Items[0].Name).IsEqualTo("Second");
    }

    [Test]
    public async Task SaveAsync_ThenLoadAsync_ShouldRoundTripEveryField()
    {
        string path = CreateTempFilePath();
        Guid id;

        using (var writer = new StreamManagerService { FilePath = path })
        {
            var item = new StreamItem
            {
                Name = "直播一台",
                Url = "https://example.com/live/ch1.m3u8",
                FormatType = StreamFormatType.Hls,
                UserAgent = "VLC/3.0",
                Referer = "https://example.com/",
                Headers = "Cookie: a=b",
            };
            id = item.Id;

            writer.Add(item);
            await writer.SaveAsync();
        }

        using var reader = new StreamManagerService { FilePath = path };
        await reader.LoadAsync();

        await Assert.That(reader.Items.Count).IsEqualTo(1);
        StreamItem loaded = reader.Items[0];
        await Assert.That(loaded.Id).IsEqualTo(id);
        await Assert.That(loaded.Name).IsEqualTo("直播一台");
        await Assert.That(loaded.FormatType).IsEqualTo(StreamFormatType.Hls);
        await Assert.That(loaded.UserAgent).IsEqualTo("VLC/3.0");
        await Assert.That(loaded.Referer).IsEqualTo("https://example.com/");
        await Assert.That(loaded.Headers).IsEqualTo("Cookie: a=b");
        await Assert.That(loaded.BadgeText).IsEqualTo("HLS");
    }

    [Test]
    public async Task LoadAsync_WhenFileMissing_ShouldLeaveListEmpty()
    {
        using var service = new StreamManagerService { FilePath = CreateTempFilePath() };
        await service.LoadAsync();

        await Assert.That(service.Items.Count).IsEqualTo(0);
        await Assert.That(service.IsLoaded).IsTrue();
    }

    [Test]
    public async Task LoadAsync_WhenJsonIsBroken_ShouldStartEmptyAndKeepBackup()
    {
        string path = CreateTempFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ this is not json");

        using var service = new StreamManagerService { FilePath = path };
        await service.LoadAsync();

        await Assert.That(service.Items.Count).IsEqualTo(0);
        await Assert.That(File.Exists(path + ".corrupt")).IsTrue();
    }

    [Test]
    public async Task LoadAsync_WhenEntriesLackName_ShouldFallBackToUrl()
    {
        string path = CreateTempFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            [
              { "Id": "11111111-1111-1111-1111-111111111111", "Url": "https://example.com/a.m3u8", "FormatType": "Hls" }
            ]
            """);

        using var service = new StreamManagerService { FilePath = path };
        await service.LoadAsync();

        await Assert.That(service.Items[0].Name).IsEqualTo("https://example.com/a.m3u8");
    }

    [Test]
    public async Task Remove_ShouldDetachEntryFromList()
    {
        using var service = new StreamManagerService { FilePath = CreateTempFilePath() };
        StreamItem item = service.Add(new StreamItem { Name = "A", Url = "https://example.com/a.mp4" });

        service.Remove(item);

        await Assert.That(service.Items.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Rename_ShouldTrimAndIgnoreBlank()
    {
        using var service = new StreamManagerService { FilePath = CreateTempFilePath() };
        StreamItem item = service.Add(new StreamItem { Name = "Old", Url = "https://example.com/a.mp4" });

        service.Rename(item, "   New Name  ");
        await Assert.That(item.Name).IsEqualTo("New Name");

        service.Rename(item, "   ");
        await Assert.That(item.Name).IsEqualTo("New Name");
    }

    [Test]
    public async Task Update_ShouldCopyEditableFieldsButKeepIdentity()
    {
        using var service = new StreamManagerService { FilePath = CreateTempFilePath() };
        StreamItem item = service.Add(new StreamItem { Name = "Old", Url = "https://example.com/a.mp4" });
        Guid id = item.Id;

        var edited = new StreamItem
        {
            Id = Guid.NewGuid(),
            Name = "New",
            Url = "https://example.com/b.m3u8",
            FormatType = StreamFormatType.Hls,
            UserAgent = "UA",
        };

        service.Update(item, edited);

        await Assert.That(item.Id).IsEqualTo(id);
        await Assert.That(item.Name).IsEqualTo("New");
        await Assert.That(item.Url).IsEqualTo("https://example.com/b.m3u8");
        await Assert.That(item.FormatType).IsEqualTo(StreamFormatType.Hls);
        await Assert.That(item.UserAgent).IsEqualTo("UA");
    }

    [Test]
    public async Task DefaultFilePath_ShouldLiveUnderFFluxLocalAppData()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFlux",
            "streams.json");

        await Assert.That(StreamManagerService.DefaultFilePath).IsEqualTo(expected);
    }

    [Test]
    public async Task SerializerOptions_ShouldUseASourceGeneratedResolver()
    {
        // The app publishes as Native AOT, where reflection based serialization is disabled at
        // runtime. The test host still allows reflection, so only this assertion catches the
        // regression that would leave the library unsaved in the real app.
        await Assert.That(StreamManagerService.SerializerOptions.TypeInfoResolver).IsNotNull();
    }

    [Test]
    public async Task SerializerOptions_ShouldKeepCjkNamesReadable()
    {
        var item = new StreamItem { Name = "直播一台", Url = "https://example.com/a.m3u8" };

        string json = JsonSerializer.Serialize(new List<StreamItem> { item },
            StreamManagerService.SerializerOptions);

        await Assert.That(json).Contains("直播一台");
        await Assert.That(json).DoesNotContain("\\u");
    }
}
