using Shouldly;
using Xunit;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolHtmlEmbeddedJsonExtractorTests
{
    [Fact]
    public void Should_Extract_Next_Data_Script_Body()
    {
        var html =
            """<!doctype html><html><body><script id="__NEXT_DATA__" type="application/json">{"hello":"world"}</script></body></html>""";

        var json = TrendyolHtmlEmbeddedJsonExtractor.TryExtractNextDataJson(html);

        json.ShouldNotBeNull();
        json.ShouldContain("\"hello\"");
    }

    [Fact]
    public void Should_Return_Null_When_No_Next_Data()
    {
        var html = "<html><body></body></html>";

        TrendyolHtmlEmbeddedJsonExtractor.TryExtractNextDataJson(html).ShouldBeNull();
    }
}
