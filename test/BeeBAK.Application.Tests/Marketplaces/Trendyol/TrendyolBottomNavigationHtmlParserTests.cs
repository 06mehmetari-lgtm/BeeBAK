using Shouldly;
using Xunit;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolBottomNavigationHtmlParserTests
{
    private readonly TrendyolBottomNavigationHtmlParser _parser = new();

    [Fact]
    public void Should_Parse_Butik_List_Sections_With_Id_As_Key()
    {
        var html =
            @"<div class=""sections-wrapper""><a class=""section-item active"" href=""/butik/liste/1/kadin""><p class=""section-name"">Kadın</p></a>" +
            @"<a class=""section-item"" href=""/butik/liste/22/spor-outdoor""><p class=""section-name"">Spor &amp; Outdoor</p></a></div>";

        var map = _parser.ParseSectionItems(html);

        map.ShouldContainKey("1");
        map["1"].DisplayName.ShouldBe("Kadın");
        map["1"].Slug.ShouldBe("kadin");

        map.ShouldContainKey("22");
        map["22"].DisplayName.ShouldBe("Spor & Outdoor");
        map["22"].Slug.ShouldBe("spor-outdoor");
    }

    [Fact]
    public void Should_Parse_Flash_And_Bestseller_Urls()
    {
        var html =
            @"<a class=""section-item"" href=""/flas-indirimler""><p class=""section-name"">Flaş Ürünler</p></a>" +
            @"<a class=""section-item"" href=""/cok-satanlar?type=bestSeller&amp;webGenderId=1""><p class=""section-name"">Çok Satanlar</p></a>";

        var map = _parser.ParseSectionItems(html);

        map.ShouldContainKey("flas-indirimler");
        map["flas-indirimler"].DisplayName.ShouldBe("Flaş Ürünler");

        map.ShouldContainKey("cok-satanlar:w1");
        map["cok-satanlar:w1"].DisplayName.ShouldBe("Çok Satanlar");
    }
}
