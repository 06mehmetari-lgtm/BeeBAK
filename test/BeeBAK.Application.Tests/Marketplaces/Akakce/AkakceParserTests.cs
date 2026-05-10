using System;
using System.Linq;
using BeeBAK.Marketplaces.Akakce;
using Shouldly;
using Xunit;

namespace BeeBAK.Marketplaces.Akakce;

public class AkakceParserTests
{
    [Fact]
    public void Listing_Parser_Should_Parse_Product_Cards()
    {
        const string html = """
            <ul id="PDL">
              <li data-pr="123456" data-mk="Samsung">
                <a class="iC" href="/televizyon/en-ucuz-samsung-tv-fiyati,123456.html">
                  <img data-src="//cdn.akakce.com/img.jpg" />
                  <h3 class="pn_v8">Samsung 55 inch TV</h3>
                </a>
                <span class="db_v9"><i>%35</i></span>
                <span class="pt_v9">18.999,90 TL +3 FIYAT</span>
              </li>
            </ul>
            """;

        var cards = AkakceListingHtmlParser.Parse(html, "https://www.akakce.com");

        cards.Count.ShouldBe(1);
        var card = cards.Single();
        card.ProductCode.ShouldBe("123456");
        card.ProductUrl.ShouldBe("https://www.akakce.com/televizyon/en-ucuz-samsung-tv-fiyati,123456.html");
        card.Title.ShouldBe("Samsung 55 inch TV");
        card.BrandName.ShouldBe("Samsung");
        card.ImageUrl.ShouldBe("https://cdn.akakce.com/img.jpg");
        card.DiscountPercent.ShouldBe(35);
        card.BestPriceAmount.ShouldBe(18999.90m);
        card.OfferCount.ShouldBe(4);
    }

    [Fact]
    public void Listing_Parser_Should_Skip_Cards_Without_Detail_Link()
    {
        const string html = """
            <ul id="PDL">
              <li data-pr="no-link">
                <h3 class="pn_v8">Linkless product</h3>
                <span class="pt_v9">999 TL</span>
              </li>
            </ul>
            """;

        AkakceListingHtmlParser.Parse(html, "https://www.akakce.com").ShouldBeEmpty();
    }

    [Fact]
    public void Detail_Parser_Should_Parse_Merchant_Offer_Rows()
    {
        const string html = """
            <html>
              <head>
                <meta property="og:image" content="https://cdn.akakce.com/product.jpg" />
              </head>
              <body>
                <ol><li><a>Akakce</a></li><li><a>Elektronik</a></li><li><a>Kulaklik</a></li></ol>
                <h1>Bluetooth Kulaklik</h1>
                <ul>
                  <li>
                    <img alt="TeknoMarket" />
                    <span>Bluetooth Kulaklik 1.249,50 TL ucretsiz kargo Stokta 2 is gunu Saticiya Git</span>
                    <a href="https://store.example/product-1">Saticiya Git</a>
                  </li>
                </ul>
              </body>
            </html>
            """;

        var result = AkakceProductDetailHtmlParser.TryParse(
            html,
            "https://www.akakce.com/kulaklik/en-ucuz-bluetooth-kulaklik-fiyati,555.html",
            "555",
            new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc));

        result.ShouldNotBeNull();
        result!.ProductCode.ShouldBe("555");
        result.Title.ShouldBe("Bluetooth Kulaklik");
        result.CategoryPath.ShouldBe("Elektronik > Kulaklik");
        result.PrimaryImageUrl.ShouldBe("https://cdn.akakce.com/product.jpg");
        result.Offers.Count.ShouldBe(1);
        result.Offers[0].MerchantName.ShouldBe("TeknoMarket");
        result.Offers[0].Price.ShouldBe(1249.50m);
        result.Offers[0].Currency.ShouldBe("TRY");
        result.Offers[0].ShippingText!.ShouldContain("kargo");
        result.Offers[0].StockText!.ShouldContain("Stokta");
        result.Offers[0].MerchantProductUrl.ShouldBe("https://store.example/product-1");
    }

    [Fact]
    public void Detail_Parser_Should_Ignore_Summary_And_Parent_Offer_Containers()
    {
        const string html = """
            <html>
              <body>
                <h1>Watsons Kolay Kavramali Sac Fircasi</h1>
                <div class="summary">
                  2 satici icinde kargo dahil en ucuz fiyat secenegi 99,95 TL +74,90 TL kargo
                  <a href="/c/?summary=true">Saticiya Git</a>
                </div>
                <div class="all-prices">
                  <div class="offer-row">
                    <span>99,95 TL +74,90 TL kargo</span>
                    <span>Stokta 2 adet Yarin kargoda Satici: watsons</span>
                    <a href="/c/?s=1&v=2829&p=1708964774">Saticiya Git</a>
                  </div>
                  <div class="offer-row">
                    <span>499,00 TL Ucretsiz kargo</span>
                    <span>Stokta 10 adet 2 is gunu Satici: hepsiburada</span>
                    <a href="/c/?s=2&v=12088&p=1708964774">Saticiya Git</a>
                  </div>
                </div>
              </body>
            </html>
            """;

        var result = AkakceProductDetailHtmlParser.TryParse(
            html,
            "https://www.akakce.com/sac-fircasi-taragi/en-ucuz-watsons-kolay-kavramali-sac-fircasi-fiyati,1708964774.html",
            "1708964774",
            new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc));

        result.ShouldNotBeNull();
        result!.Offers.Count.ShouldBe(2);
        result.Offers[0].MerchantName.ShouldBe("watsons");
        result.Offers[0].Price.ShouldBe(99.95m);
        result.Offers[1].MerchantName.ShouldBe("hepsiburada");
        result.Offers[1].Price.ShouldBe(499.00m);
    }
}
