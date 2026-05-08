using Shouldly;
using Xunit;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolSearchJsonParserTests
{
    private readonly TrendyolSearchJsonParser _parser = new();

    [Fact]
    public void Should_Parse_Result_Products_Array()
    {
        var json =
            """{"result":{"products":[{"id":"42","name":"Phone","price":99.9,"url":"/p/x"}]}}""";

        var items = _parser.Parse(json, "https://www.trendyol.com");

        items.Count.ShouldBe(1);
        items[0].ExternalId.ShouldBe("42");
        items[0].Title.ShouldBe("Phone");
        items[0].Price.ShouldBe(99.9m);
        items[0].ProductUrl.ShouldBe("https://www.trendyol.com/p/x");
    }

    [Fact]
    public void Should_Parse_Root_Products_Array()
    {
        var json = """{"products":[{"id":"1","title":"Book","price":10,"link":"/b/1"}]}""";

        var items = _parser.Parse(json);

        items.Count.ShouldBe(1);
        items[0].ExternalId.ShouldBe("1");
        items[0].Title.ShouldBe("Book");
        items[0].ProductUrl.ShouldContain("trendyol.com");
        items[0].ProductUrl.ShouldEndWith("/b/1");
    }

    [Fact]
    public void Should_Parse_Content_Id_And_Price_Object()
    {
        var json =
            """{"result":{"products":[{"content":{"id":"501"},"name":"Case","url":"/c","price":{"sellingPrice":49.99,"originalPrice":59.99}}]}}""";

        var items = _parser.Parse(json, "https://www.trendyol.com");

        items.Count.ShouldBe(1);
        items[0].ExternalId.ShouldBe("501");
        items[0].Price.ShouldBe(49.99m);
        items[0].ListPrice.ShouldBe(59.99m);
    }

    [Fact]
    public void Should_Return_Empty_When_No_Products()
    {
        var json = "{}";

        var items = _parser.Parse(json);

        items.ShouldBeEmpty();
    }

    [Fact]
    public void Should_Parse_Deeply_Nested_Products_Array()
    {
        var json =
            """{"props":{"pageProps":{"search":{"listing":{"products":[{"id":"9","name":"Watch","price":1,"url":"/w"}]}}}}}""";

        var items = _parser.Parse(json, "https://www.trendyol.com");

        items.Count.ShouldBe(1);
        items[0].ExternalId.ShouldBe("9");
        items[0].Title.ShouldBe("Watch");
    }
}
