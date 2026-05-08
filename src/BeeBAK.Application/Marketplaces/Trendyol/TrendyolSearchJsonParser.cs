using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Volo.Abp.DependencyInjection;

namespace BeeBAK.Marketplaces.Trendyol;

public class TrendyolSearchJsonParser : ITransientDependency
{
    private const string DefaultOrigin = "https://www.trendyol.com";

    public IReadOnlyList<TrendyolListingItem> Parse(string json, string? siteOrigin = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<TrendyolListingItem>();
        }

        using var doc = JsonDocument.Parse(json);
        if (!TryGetProductsArray(doc.RootElement, out var products))
        {
            return Array.Empty<TrendyolListingItem>();
        }

        var origin = string.IsNullOrWhiteSpace(siteOrigin) ? DefaultOrigin : siteOrigin!.TrimEnd('/');

        var list = new List<TrendyolListingItem>();
        foreach (var p in products.EnumerateArray())
        {
            if (TryMapProduct(p, origin, out var item))
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static bool TryGetProductsArray(JsonElement root, out JsonElement products)
    {
        if (TryGetArray(root, "products", out products))
        {
            return true;
        }

        if (root.TryGetProperty("result", out var result))
        {
            if (TryGetArray(result, "products", out products))
            {
                return true;
            }
        }

        if (root.TryGetProperty("data", out var data))
        {
            if (TryGetArray(data, "products", out products))
            {
                return true;
            }
        }

        foreach (var arr in EnumerateNamedArrays(root, "products", depth: 0, maxDepth: 18))
        {
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            {
                continue;
            }

            var first = arr[0];
            if (first.ValueKind == JsonValueKind.Object &&
                TryMapProduct(first, DefaultOrigin, out _))
            {
                products = arr;
                return true;
            }
        }

        products = default;
        return false;
    }

    private static IEnumerable<JsonElement> EnumerateNamedArrays(
        JsonElement element,
        string propertyName,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth)
        {
            yield break;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals(propertyName) && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        yield return prop.Value;
                    }
                }

                foreach (var prop in element.EnumerateObject())
                {
                    foreach (var nested in EnumerateNamedArrays(prop.Value, propertyName, depth + 1, maxDepth))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in EnumerateNamedArrays(item, propertyName, depth + 1, maxDepth))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static bool TryGetArray(JsonElement obj, string name, out JsonElement array)
    {
        return obj.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array;
    }

    private static bool TryMapProduct(JsonElement p, string origin, out TrendyolListingItem item)
    {
        item = default!;

        if (!TryGetExternalId(p, out var externalId))
        {
            return false;
        }

        if (!TryGetStringFlexible(p, "name", "title", out var title))
        {
            return false;
        }

        if (!TryGetStringFlexible(p, "url", "link", out var urlRaw))
        {
            return false;
        }

        var url = NormalizeUrl(urlRaw, origin);
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!TryReadPrice(p, out var price))
        {
            return false;
        }

        TryReadListPrice(p, out var listPrice);
        TryReadDiscountPercent(p, out var discountPct);

        _ = TryGetStringFlexible(p, "brandName", "brand", out var brandName);
        TryGetMerchantId(p, out var merchantId);

        string? raw = null;
        try
        {
            raw = p.GetRawText();
        }
        catch
        {
            /* ignore */
        }

        item = new TrendyolListingItem(
            externalId,
            title.Trim(),
            url,
            price,
            listPrice,
            discountPct,
            string.IsNullOrWhiteSpace(brandName) ? null : brandName.Trim(),
            merchantId,
            raw);

        return true;
    }

    private static bool TryGetExternalId(JsonElement p, out string id)
    {
        id = "";
        if (p.TryGetProperty("content", out var content))
        {
            if (TryGetStringLike(content, "id", out id))
            {
                return true;
            }

            if (content.TryGetProperty("brandId", out _))
            {
                /* continue */
            }
        }

        return TryGetStringLike(p, "id", out id)
               || TryGetStringLike(p, "productId", out id)
               || TryGetStringLike(p, "listingId", out id);
    }

    private static bool TryGetMerchantId(JsonElement p, out string? merchantId)
    {
        merchantId = null;
        if (TryGetStringFlexible(p, "merchantId", "sellerId", "merchant", out var raw))
        {
            merchantId = raw.Trim();
            return true;
        }

        if (p.TryGetProperty("merchant", out var m) && m.ValueKind == JsonValueKind.Object &&
            TryGetStringLike(m, "id", out var mid))
        {
            merchantId = mid;
            return true;
        }

        return false;
    }

    private static bool TryGetStringFlexible(JsonElement p, string a, string b, out string value)
    {
        return TryGetStringLike(p, a, out value)
               || TryGetStringLike(p, b, out value);
    }

    private static bool TryGetStringFlexible(JsonElement p, string a, string b, string c, out string value)
    {
        return TryGetStringLike(p, a, out value)
               || TryGetStringLike(p, b, out value)
               || TryGetStringLike(p, c, out value);
    }

    private static bool TryGetStringLike(JsonElement p, string name, out string value)
    {
        value = "";
        if (!p.TryGetProperty(name, out var el))
        {
            return false;
        }

        return el.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value = el.GetString() ?? ""),
            JsonValueKind.Number => !(string.IsNullOrWhiteSpace(value = el.GetRawText())),
            _ => false
        };
    }

    private static string NormalizeUrl(string urlRaw, string origin)
    {
        var t = urlRaw.Trim();
        if (t.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + t;
        }

        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return t;
        }

        if (t.StartsWith('/'))
        {
            return $"{origin}{t}";
        }

        return $"{origin}/{t}";
    }

    private static bool TryReadPrice(JsonElement p, out decimal price)
    {
        price = 0m;

        if (!p.TryGetProperty("price", out var priceEl))
        {
            return TryGetDecimalDirect(p, "sellingPrice", out price)
                   || TryGetDecimalDirect(p, "discountedPrice", out price)
                   || TryGetDecimalDirect(p, "salePrice", out price);
        }

        if (priceEl.ValueKind == JsonValueKind.Number && TryGetDecimal(priceEl, out price))
        {
            return true;
        }

        if (priceEl.ValueKind == JsonValueKind.Object)
        {
            if (TryGetDecimalNested(priceEl, "sellingPrice", "value", out price)
                || TryGetDecimalNested(priceEl, "discountedPrice", "value", out price)
                || TryGetDecimalNested(priceEl, "salePrice", "value", out price)
                || TryGetDecimalNested(priceEl, "discountedPrice", out price)
                || TryGetDecimalNested(priceEl, "sellingPrice", out price))
            {
                return true;
            }
        }

        return TryGetDecimalDirect(p, "sellingPrice", out price);
    }

    private static bool TryReadListPrice(JsonElement p, out decimal? listPrice)
    {
        listPrice = null;
        if (!p.TryGetProperty("price", out var priceEl) || priceEl.ValueKind != JsonValueKind.Object)
        {
            if (TryGetDecimalDirect(p, "originalPrice", out var op))
            {
                listPrice = op;
                return true;
            }

            if (TryGetDecimalDirect(p, "listPrice", out var lp))
            {
                listPrice = lp;
                return true;
            }

            return false;
        }

        if (TryGetDecimalNested(priceEl, "originalPrice", "value", out var v)
            || TryGetDecimalNested(priceEl, "originalPrice", out v)
            || TryGetDecimalNested(priceEl, "marketPrice", out v))
        {
            listPrice = v;
            return true;
        }

        return false;
    }

    private static bool TryReadDiscountPercent(JsonElement p, out decimal? discount)
    {
        discount = null;
        if (TryGetDecimalDirect(p, "discountPercentage", out var d))
        {
            discount = d;
            return true;
        }

        if (p.TryGetProperty("price", out var priceEl) && priceEl.ValueKind == JsonValueKind.Object &&
            TryGetDecimalNested(priceEl, "discountPercentage", out var dp))
        {
            discount = dp;
            return true;
        }

        return false;
    }

    private static bool TryGetDecimalDirect(JsonElement p, string name, out decimal value)
    {
        value = 0m;
        return p.TryGetProperty(name, out var el) && TryGetDecimal(el, out value);
    }

    private static bool TryGetDecimalNested(JsonElement obj, string prop, out decimal value)
    {
        value = 0m;
        return obj.TryGetProperty(prop, out var el) && TryGetDecimal(el, out value);
    }

    private static bool TryGetDecimalNested(JsonElement obj, string prop, string nested, out decimal value)
    {
        value = 0m;
        if (!obj.TryGetProperty(prop, out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(nested, out var inner))
        {
            return TryGetDecimal(inner, out value);
        }

        return TryGetDecimal(el, out value);
    }

    private static bool TryGetDecimal(JsonElement el, out decimal value)
    {
        value = 0m;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (el.TryGetDecimal(out value))
                {
                    return true;
                }

                break;
            case JsonValueKind.String:
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s) &&
                    decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(s) &&
                    decimal.TryParse(s, NumberStyles.Any, new CultureInfo("tr-TR"), out value))
                {
                    return true;
                }

                break;
        }

        return false;
    }
}
