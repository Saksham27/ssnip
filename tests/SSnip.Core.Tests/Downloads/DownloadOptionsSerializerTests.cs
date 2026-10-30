using System.Text.Json;
using Shouldly;
using SSnip.Core.Downloads;

namespace SSnip.Core.Tests.Downloads;

public class DownloadOptionsSerializerTests
{
    [Fact]
    public void Round_trips_every_option_to_the_same_arguments()
    {
        var options = new DownloadOptions
        {
            Url = "https://www.example.com/watch?v=TestVid0001",
            OutputDirectory = @"E:\Footage\रीकैप\raw",
            Preset = FormatPreset.Custom,
            Custom = new FormatSelection("137", "140", "res:1080"),
            FilenameTemplate = "%(upload_date)s - %(title)s [%(id)s].%(ext)s",
            MergeContainer = "mkv",
            AudioFormat = AudioFormat.Flac,
            Overwrite = true,
            RateLimit = "5M",
            Sections = ["*0:10-0:20"],
            Subtitles = new SubtitleOptions(["en", "hi"], IncludeAutomatic: false, SubtitleMode.SrtAndEmbed),
            EmbedMetadata = true,
            EmbedThumbnail = true,
            EmbedChapters = true,
            InfoJsonDirectory = @"C:\Data\info",
            MaxComments = 50,
            SponsorBlock = SponsorBlockOptions.FootageDefault,
            DateAfter = new DateOnly(2025, 1, 1),
            Live = new LiveOptions(true, 60),
        };

        var copy = DownloadOptionsSerializer.Deserialize(DownloadOptionsSerializer.Serialize(options));

        YtDlpArgsBuilder.Build(copy).ShouldBe(YtDlpArgsBuilder.Build(options));
        copy.Sections.ShouldBe(options.Sections);
        copy.Subtitles!.Languages.ShouldBe(["en", "hi"]);
    }

    [Fact]
    public void Missing_fields_keep_their_defaults()
    {
        var options = DownloadOptionsSerializer.Deserialize("""{"url": "https://www.example.com/v", "outputDirectory": "E:\\raw"}""");

        options.Preset.ShouldBe(FormatPreset.EditorSafe);
        options.ConcurrentFragments.ShouldBe(4);
        options.Sections.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_url_throws() =>
        Should.Throw<JsonException>(() => DownloadOptionsSerializer.Deserialize("""{"outputDirectory": "E:\\raw"}"""));
}
