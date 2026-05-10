using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Angular'daki share-card tasarımını SkiaSharp ile sunucu tarafında render eder.
/// Çıktı PNG byte dizisidir; Telegram'a doğrudan upload edilir.
/// </summary>
public static class CimriCardImageGenerator
{
    private const int W = 440;
    private const int H = 272;
    private const int Radius = 18;
    private const int PadH = 16;
    private const float BrandH = 50f;
    private const float FooterH = 32f;
    private const float ImgColW = 148f;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // ── Tema renkleri (ember, aurora, tide, citrus döngüsü) ──────────────
    private static readonly (SKColor top, SKColor bottom, SKColor accent)[] Themes =
    [
        (new SKColor(0xB9, 0x1C, 0x1C), new SKColor(0x1C, 0x0A, 0x0A), new SKColor(0xFB, 0xBF, 0x24)), // ember
        (new SKColor(0x5B, 0x21, 0xB6), new SKColor(0x0F, 0x07, 0x20), new SKColor(0x8B, 0x5C, 0xF6)), // aurora
        (new SKColor(0x03, 0x69, 0xA1), new SKColor(0x04, 0x2F, 0x2E), new SKColor(0x38, 0xBD, 0xF8)), // tide
        (new SKColor(0xC2, 0x41, 0x0C), new SKColor(0x1C, 0x10, 0x08), new SKColor(0xFB, 0x92, 0x3C)), // citrus
    ];

    public static async Task<byte[]> GenerateAsync(
        string productTitle,
        string? productImageUrl,
        decimal lowestPrice,
        decimal? avgPrice,
        decimal? bestPriceFrom,
        string currency,
        string? merchantName,
        decimal? discountPercent,
        int themeIndex,
        HttpClient httpClient,
        CancellationToken ct = default)
    {
        var theme = Themes[themeIndex % Themes.Length];

        SKBitmap? productBitmap = null;
        if (!string.IsNullOrWhiteSpace(productImageUrl))
        {
            try
            {
                var imgBytes = await httpClient.GetByteArrayAsync(productImageUrl, ct);
                productBitmap = SKBitmap.Decode(imgBytes);
            }
            catch { /* görsel yoksa fallback emoji */ }
        }

        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(W, H));
            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);

            DrawBackground(c, theme);
            DrawGoldAccentLine(c, theme.accent);
            DrawBrandHeader(c, theme.accent);
            DrawSeparator(c, BrandH);
            DrawContent(c, productBitmap, productTitle, lowestPrice, avgPrice, bestPriceFrom, currency, merchantName, discountPercent, theme.accent);
            DrawFooterSeparator(c);
            DrawFooterCta(c);

            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 95);
            return data.ToArray();
        }
        finally
        {
            productBitmap?.Dispose();
        }
    }

    // ── Arka plan: koyu gradient + yatay parlaklık ───────────────────────
    private static void DrawBackground(SKCanvas c, (SKColor top, SKColor bottom, SKColor accent) theme)
    {
        var rect = new SKRect(0, 0, W, H);
        using var rrect = new SKRoundRect(rect, Radius, Radius);

        using var paint = new SKPaint { IsAntialias = true };
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(W, H),
            [theme.top, theme.bottom],
            SKShaderTileMode.Clamp);
        paint.Shader = shader;
        c.DrawRoundRect(rrect, paint);

        // Üst orta parlaklık highlight
        using var highlightPaint = new SKPaint { IsAntialias = true };
        using var hlShader = SKShader.CreateRadialGradient(
            new SKPoint(W * 0.5f, -H * 0.1f), W * 0.9f,
            [new SKColor(255, 255, 255, 38), new SKColor(255, 255, 255, 0)],
            SKShaderTileMode.Clamp);
        highlightPaint.Shader = hlShader;
        highlightPaint.BlendMode = SKBlendMode.SoftLight;
        c.DrawRoundRect(rrect, highlightPaint);
    }

    // ── Üst altın accent çizgi ──────────────────────────────────────────
    private static void DrawGoldAccentLine(SKCanvas c, SKColor accent)
    {
        var lineRect = new SKRect(W * 0.1f, 0, W * 0.9f, 3);
        using var paint = new SKPaint { IsAntialias = true };
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(lineRect.Left, 0), new SKPoint(lineRect.Right, 0),
            [new SKColor(accent.Red, accent.Green, accent.Blue, 0),
             new SKColor(accent.Red, accent.Green, accent.Blue, 230),
             new SKColor(accent.Red, accent.Green, accent.Blue, 0)],
            SKShaderTileMode.Clamp);
        paint.Shader = shader;
        c.DrawRect(lineRect, paint);
    }

    // ── Brand header: "B" rozeti + "Bee BAK Sana" ────────────────────────
    private static void DrawBrandHeader(SKCanvas c, SKColor accent)
    {
        var cy = BrandH / 2f;
        var badgeCx = (float)PadH + 17f;

        // "B" rozeti çemberi
        using var circlePaint = new SKPaint { IsAntialias = true };
        using var circleShader = SKShader.CreateLinearGradient(
            new SKPoint(badgeCx - 17, cy - 17), new SKPoint(badgeCx + 17, cy + 17),
            [new SKColor(255, 255, 255, 55), new SKColor(255, 255, 255, 15)],
            SKShaderTileMode.Clamp);
        circlePaint.Shader = circleShader;
        c.DrawCircle(badgeCx, cy, 16f, circlePaint);

        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color = new SKColor(accent.Red, accent.Green, accent.Blue, 140),
        };
        c.DrawCircle(badgeCx, cy, 16f, borderPaint);

        DrawText(c, "B", badgeCx, cy + 5f, SKColors.White, 14f, bold: true, hAlign: SKTextAlign.Center);

        // Marka adı
        DrawText(c, "Bee BAK Sana", badgeCx + 22f, cy + 5f, SKColors.White, 13f, bold: true);
    }

    // ── Yatay ayırıcı çizgi ───────────────────────────────────────────────
    private static void DrawSeparator(SKCanvas c, float y)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 30),
            StrokeWidth = 1f,
        };
        c.DrawLine(PadH, y, W - PadH, y, paint);
    }

    // ── Ana içerik: görsel + fiyat paneli ───────────────────────────────
    private static void DrawContent(
        SKCanvas c,
        SKBitmap? productBitmap,
        string title,
        decimal lowest,
        decimal? avg,
        decimal? bestPriceFrom,
        string currency,
        string? merchant,
        decimal? discountPct,
        SKColor accent)
    {
        var contentTop = BrandH + 8f;
        var contentBottom = H - FooterH - 8f;
        var contentH = contentBottom - contentTop;

        // Sol: ürün görseli
        DrawProductImage(c, productBitmap, PadH, contentTop, ImgColW - PadH * 0.5f, contentH, discountPct);

        // Sağ: başlık + fiyat chip'leri
        var captionLeft = PadH + ImgColW + 6f;
        var captionRight = W - PadH;
        DrawCaptionPanel(c, captionLeft, contentTop, captionRight, contentBottom, title, lowest, avg, bestPriceFrom, currency, merchant, accent);
    }

    // ── Ürün görseli (beyaz yuvarlak kutu) ──────────────────────────────
    private static void DrawProductImage(
        SKCanvas c, SKBitmap? bmp, float x, float y, float w, float h, decimal? discountPct)
    {
        var imgSize = Math.Min(w, h);
        var imgRect = new SKRect(x, y + (h - imgSize) * 0.5f, x + imgSize, y + (h - imgSize) * 0.5f + imgSize);

        // Beyaz arka plan
        using var bgPaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        c.DrawRoundRect(new SKRoundRect(imgRect, 12, 12), bgPaint);

        // Kenarlık + hafif gölge
        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            Color = new SKColor(255, 255, 255, 215),
        };
        c.DrawRoundRect(new SKRoundRect(imgRect, 12, 12), borderPaint);

        if (bmp != null)
        {
            c.Save();
            c.ClipRoundRect(new SKRoundRect(imgRect, 12, 12), antialias: true);
            var srcRect = new SKRect(0, 0, bmp.Width, bmp.Height);
            var side = Math.Min(bmp.Width, bmp.Height);
            var srcCrop = new SKRect(
                (bmp.Width - side) * 0.5f,
                (bmp.Height - side) * 0.5f,
                (bmp.Width - side) * 0.5f + side,
                (bmp.Height - side) * 0.5f + side);
            using var imgPaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            c.DrawBitmap(bmp, srcCrop, imgRect, imgPaint);
            c.Restore();
        }
        else
        {
            DrawText(c, "📦", imgRect.MidX, imgRect.MidY + 10f, new SKColor(0, 0, 0, 100), 28f, hAlign: SKTextAlign.Center);
        }

        // İndirim rozeti
        if (discountPct is > 0)
        {
            DrawDiscountBadge(c, $"−{(int)discountPct}%", imgRect.Right - 4f, imgRect.Top + 4f);
        }
    }

    // ── İndirim rozeti (kırmızı pill) ────────────────────────────────────
    private static void DrawDiscountBadge(SKCanvas c, string text, float right, float top)
    {
        using var textPaint = new SKPaint { IsAntialias = true };
        using var tf = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var font = new SKFont(tf, 9f);

        var textW = font.MeasureText(text);
        var padX = 6f;
        var badgeW = textW + padX * 2f;
        var badgeH = 17f;
        var badgeRect = new SKRect(right - badgeW, top, right, top + badgeH);

        using var bgPaint = new SKPaint { IsAntialias = true };
        using var bgShader = SKShader.CreateLinearGradient(
            new SKPoint(badgeRect.Left, badgeRect.Top), new SKPoint(badgeRect.Right, badgeRect.Bottom),
            [new SKColor(0x99, 0x1B, 0x1B), new SKColor(0x45, 0x0A, 0x0A)],
            SKShaderTileMode.Clamp);
        bgPaint.Shader = bgShader;
        c.DrawRoundRect(new SKRoundRect(badgeRect, 8f, 8f), bgPaint);

        using var bordPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = new SKColor(0xFC, 0xD3, 0x4D, 160),
        };
        c.DrawRoundRect(new SKRoundRect(badgeRect, 8f, 8f), bordPaint);

        DrawText(c, text, badgeRect.MidX, badgeRect.MidY + 3.5f, new SKColor(0xFF, 0xFB, 0xEB), 9f, bold: true, hAlign: SKTextAlign.Center);
    }

    // ── Başlık + fiyat chip paneli ──────────────────────────────────────
    private static void DrawCaptionPanel(
        SKCanvas c, float left, float top, float right, float bottom,
        string title, decimal lowest, decimal? avg, decimal? bestPriceFrom,
        string currency, string? merchant, SKColor accent)
    {
        var panelRect = new SKRect(left, top, right, bottom);

        // Panel arka planı
        using var panelPaint = new SKPaint { IsAntialias = true };
        using var panelShader = SKShader.CreateLinearGradient(
            new SKPoint(left, top), new SKPoint(left, bottom),
            [new SKColor(0x1E, 0x29, 0x3B, 230), new SKColor(0x0F, 0x17, 0x2A, 245)],
            SKShaderTileMode.Clamp);
        panelPaint.Shader = panelShader;
        c.DrawRoundRect(new SKRoundRect(panelRect, 12, 12), panelPaint);

        using var panelBorderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = new SKColor(0xD4, 0xAF, 0x37, 105),
        };
        c.DrawRoundRect(new SKRoundRect(panelRect, 12, 12), panelBorderPaint);

        var padX = 8f;
        var padY = 7f;
        var cx = left + padX;
        var cy = top + padY;
        var contentW = right - left - padX * 2f;

        // Ürün başlığı (2 satır, küçük font)
        cy = DrawWrappedText(c, title, cx, cy, contentW, 10f, new SKColor(0xF8, 0xFA, 0xFC), 2);
        cy += 6f;

        // Ortalama fiyat chip (sarı/amber)
        if (avg.HasValue)
        {
            cy = DrawPriceChip(c, cx, cy, contentW,
                "Ortalama Fiyat", FormatMoney(avg.Value, currency),
                new SKColor(0x71, 0x3F, 0x12, 110), new SKColor(0xFD, 0xE6, 0x8A), false);
            cy += 4f;
        }

        // En uygun fiyat chip (yeşil glow)
        cy = DrawPriceChip(c, cx, cy, contentW,
            "En Uygun Fiyat", FormatMoney(lowest, currency),
            new SKColor(0x14, 0x53, 0x2D, 170), new SKColor(0xEC, 0xFD, 0xF5), true,
            bestPriceFrom.HasValue ? $"{FormatMoney(bestPriceFrom.Value, currency)} → {FormatMoney(lowest, currency)}" : null,
            currency);
        cy += 5f;

        // Mağaza adı
        if (!string.IsNullOrWhiteSpace(merchant))
        {
            DrawText(c, merchant, cx, cy + 9f, new SKColor(0x94, 0xA3, 0xB8), 9f);
        }
    }

    // ── Fiyat chip'i ─────────────────────────────────────────────────────
    private static float DrawPriceChip(
        SKCanvas c, float x, float y, float w,
        string label, string value,
        SKColor bgColor, SKColor valueColor, bool glowing,
        string? dropText = null, string? _currency = null)
    {
        var chipH = dropText != null ? 44f : 34f;
        var chipRect = new SKRect(x, y, x + w, y + chipH);

        using var bgPaint = new SKPaint { IsAntialias = true, Color = bgColor };
        c.DrawRoundRect(new SKRoundRect(chipRect, 8, 8), bgPaint);

        if (glowing)
        {
            using var borderPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                Color = new SKColor(0x34, 0xD3, 0x99, 180),
            };
            c.DrawRoundRect(new SKRoundRect(chipRect, 8, 8), borderPaint);
        }

        var px = x + 7f;
        DrawText(c, label, px, y + 12f, new SKColor(0xE2, 0xE8, 0xF0, 230), 7.5f, bold: true);
        DrawText(c, value, px, y + 26f, valueColor, glowing ? 13f : 11f, bold: true);

        if (!string.IsNullOrWhiteSpace(dropText))
        {
            using var linePaint = new SKPaint
            {
                Color = new SKColor(0x10, 0xB9, 0x81, 70),
                StrokeWidth = 1f,
            };
            c.DrawLine(x + 4f, y + chipH - 13f, x + w - 4f, y + chipH - 13f, linePaint);
            DrawText(c, dropText!, px, y + chipH - 4f, new SKColor(0xD1, 0xFA, 0xE5, 220), 7.5f);
        }

        return y + chipH;
    }

    // ── Footer ayırıcı ───────────────────────────────────────────────────
    private static void DrawFooterSeparator(SKCanvas c)
    {
        var y = H - FooterH;
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 36),
            StrokeWidth = 1f,
        };
        c.DrawLine(PadH, y, W - PadH, y, paint);
    }

    // ── CTA metni ────────────────────────────────────────────────────────
    private static void DrawFooterCta(SKCanvas c)
    {
        var cy = H - FooterH * 0.5f + 5f;
        DrawText(c, "En uygun teklife git →", W * 0.5f, cy, new SKColor(0xFE, 0xF9, 0xC3), 11f, bold: true, hAlign: SKTextAlign.Center);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────
    private static void DrawText(
        SKCanvas c, string text, float x, float y,
        SKColor color, float size,
        bool bold = false,
        SKTextAlign hAlign = SKTextAlign.Left)
    {
        using var tf = SKTypeface.FromFamilyName(null, bold ? SKFontStyle.Bold : SKFontStyle.Normal);
        using var font = new SKFont(tf, size);
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        c.DrawText(text, x, y, hAlign, font, paint);
    }

    /// <summary>Metni belirtilen genişliğe sığacak şekilde en fazla <paramref name="maxLines"/> satıra böler; son satır kesilirse "…" eklenir.</summary>
    private static float DrawWrappedText(
        SKCanvas c, string text, float x, float startY,
        float maxW, float size, SKColor color, int maxLines)
    {
        using var tf = SKTypeface.FromFamilyName(null, SKFontStyle.Normal);
        using var font = new SKFont(tf, size);
        using var paint = new SKPaint { IsAntialias = true, Color = color };

        var lineH = size * 1.45f;
        var words = text.Split(' ');
        var line = "";
        var lines = new System.Collections.Generic.List<string>();

        foreach (var word in words)
        {
            var test = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureText(test) <= maxW)
            {
                line = test;
            }
            else
            {
                if (line.Length > 0) lines.Add(line);
                line = word;
            }
            if (lines.Count >= maxLines) break;
        }
        if (line.Length > 0 && lines.Count < maxLines) lines.Add(line);

        // Son satır taşıyorsa kırp
        if (lines.Count == maxLines && font.MeasureText(lines[maxLines - 1]) > maxW)
        {
            var last = lines[maxLines - 1];
            while (last.Length > 0 && font.MeasureText(last + "…") > maxW)
                last = last[..^1];
            lines[maxLines - 1] = last + "…";
        }

        var y = startY;
        foreach (var l in lines)
        {
            c.DrawText(l, x, y + lineH - 2f, SKTextAlign.Left, font, paint);
            y += lineH;
        }
        return y;
    }

    private static string FormatMoney(decimal amount, string currency)
    {
        try { return amount.ToString("N2", Tr) + " " + currency.ToUpperInvariant(); }
        catch { return $"{amount:0.##} {currency}"; }
    }
}
