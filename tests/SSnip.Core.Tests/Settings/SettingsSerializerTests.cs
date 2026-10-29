using System.Text.Json;
using Shouldly;
using SSnip.Core.Settings;

namespace SSnip.Core.Tests.Settings;

public class SettingsSerializerTests
{
    [Fact]
    public void Round_trips()
    {
        var settings = new AppSettings { LibraryRoot = @"D:\Footage\गेमिंग", Theme = AppTheme.Light, FirstRunCompleted = true };

        SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings)).ShouldBe(settings);
    }

    [Fact]
    public void Writes_readable_camel_case_json()
    {
        var json = SettingsSerializer.Serialize(new AppSettings { Theme = AppTheme.System });

        json.ShouldContain("\"theme\": \"system\"");
        json.ShouldContain("\"firstRunCompleted\": false");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Empty_input_gives_defaults(string json) =>
        SettingsSerializer.Deserialize(json).ShouldBe(new AppSettings());

    [Fact]
    public void Missing_fields_keep_defaults()
    {
        var settings = SettingsSerializer.Deserialize("""{ "libraryRoot": "D:\\Footage", /* comment */ }""");

        settings.LibraryRoot.ShouldBe(@"D:\Footage");
        settings.Theme.ShouldBe(AppTheme.Dark);
    }

    [Fact]
    public void Invalid_json_throws() =>
        Should.Throw<JsonException>(() => SettingsSerializer.Deserialize("{ not json"));
}
