using System.Text.Json;
using Emby.Subtitle.ThunderCat.Models;

namespace Emby.Subtitle.ThunderCat.Tests;

public sealed class ThunderContractTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void SuccessFixture_UsesCodeZeroAndPreservesEscapedUnicodeText()
    {
        var json = ReadFixture("success.json");
        var response = JsonSerializer.Deserialize<ThunderSubtitleResponse>(json, Options);

        Assert.NotNull(response);
        Assert.Equal(0, response!.Code);
        var item = Assert.Single(response.Data!);
        Assert.Equal("LULU-421 \"中文字幕\".srt", item.Name);
        Assert.Equal("srt", item.Ext);
        Assert.Equal("ok", response.Result);
    }

    [Fact]
    public void EmptyFixture_IsValidSuccessfulResponse()
    {
        var json = ReadFixture("empty.json");
        var response = JsonSerializer.Deserialize<ThunderSubtitleResponse>(json, Options);

        Assert.NotNull(response);
        Assert.Equal(0, response!.Code);
        Assert.Empty(response.Data!);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Thunder", name));
}
