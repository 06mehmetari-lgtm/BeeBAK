using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace BeeBAK.Marketplaces.Cimri;

/// <summary>
/// Kare (500×500) ürün kartı: görsel tüm yüzeyi kaplar, altta koyu gradient
/// overlay üzerinde marka + fiyat bilgisi yer alır.
/// </summary>
public static class CimriCardImageGenerator
{
    private const int W = 500;
    private const int H = 500;
    private const int Radius = 20;

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    // Tema accent renkleri (ember · aurora · tide · citrus)
    private static readonly SKColor[] AccentColors =
    [
        new SKColor(0xFB, 0xBF, 0x24), // amber
        new SKColor(0x8B, 0x5C, 0xF6), // violet
        new SKColor(0x38, 0xBD, 0xF8), // sky
        new SKColor(0xFB, 0x92, 0x3C), // orange
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
        var accent = AccentColors[themeIndex % AccentColors.Length];

        SKBitmap? productBitmap = null;
        if (!string.IsNullOrWhiteSpace(productImageUrl))
        {
            try
            {
                var imgBytes = await httpClient.GetByteArrayAsync(productImageUrl, ct);
                productBitmap = SKBitmap.Decode(imgBytes);
            }
            catch { }
        }

        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(W, H));
            var c = surface.Canvas;
            c.Clear(SKColors.Black);

            // 1. Ürün görseli — tam kaplama
            DrawProductPhoto(c, productBitmap);

            // 2. Üst accent çizgisi
            DrawTopAccentLine(c, accent);

            // 3. İndirim rozeti (sağ üst)
            if (discountPercent is > 0)
                DrawDiscountBadge(c, $"%{(int)discountPercent} avantaj");

            // 4. Alt gradient overlay + metin
            DrawBottomOverlay(c, productTitle, lowestPrice, avgPrice, bestPriceFrom, currency, merchantName, accent);

            // 5. Çapraz watermark (şeffaf, premium)
            DrawWatermark(c);

            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 95);
            return data.ToArray();
        }
        finally
        {
            productBitmap?.Dispose();
        }
    }

    // ── Ürün görseli: tüm kareyi kaplar, merkez-kırp ─────────────────────
    private static void DrawProductPhoto(SKCanvas c, SKBitmap? bmp)
    {
        var cardRect = new SKRect(0, 0, W, H);
        using var rrect = new SKRoundRect(cardRect, Radius, Radius);

        c.Save();
        c.ClipRoundRect(rrect, antialias: true);

        if (bmp != null)
        {
            var side = Math.Min(bmp.Width, bmp.Height);
            var srcCrop = new SKRect(
                (bmp.Width - side) * 0.5f,
                (bmp.Height - side) * 0.5f,
                (bmp.Width - side) * 0.5f + side,
                (bmp.Height - side) * 0.5f + side);
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            c.DrawBitmap(bmp, srcCrop, cardRect, paint);
        }
        else
        {
            // Görsel yoksa koyu gradient arka plan
            using var paint = new SKPaint { IsAntialias = true };
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(W, H),
                [new SKColor(0x1E, 0x29, 0x3B), new SKColor(0x0F, 0x17, 0x2A)],
                SKShaderTileMode.Clamp);
            paint.Shader = shader;
            c.DrawRoundRect(rrect, paint);
            DrawText(c, "📦", W * 0.5f, H * 0.5f + 20f, new SKColor(255, 255, 255, 60), 64f, hAlign: SKTextAlign.Center);
        }

        c.Restore();
    }

    // ── Üst accent çizgisi ────────────────────────────────────────────────
    private static void DrawTopAccentLine(SKCanvas c, SKColor accent)
    {
        var lineRect = new SKRect(W * 0.1f, 0, W * 0.9f, 3.5f);
        using var paint = new SKPaint { IsAntialias = true };
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(lineRect.Left, 0), new SKPoint(lineRect.Right, 0),
            [new SKColor(accent.Red, accent.Green, accent.Blue, 0),
             new SKColor(accent.Red, accent.Green, accent.Blue, 240),
             new SKColor(accent.Red, accent.Green, accent.Blue, 0)],
            SKShaderTileMode.Clamp);
        paint.Shader = shader;
        c.DrawRect(lineRect, paint);
    }

    // ── İndirim rozeti (sağ üst köşe) ─────────────────────────────────────
    private static void DrawDiscountBadge(SKCanvas c, string text)
    {
        using var tf = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var font = new SKFont(tf, 13f);
        var textW = font.MeasureText(text);
        var padX = 10f;
        var badgeW = textW + padX * 2f;
        const float badgeH = 26f;
        const float right = W - 14f;
        const float top = 14f;
        var badgeRect = new SKRect(right - badgeW, top, right, top + badgeH);

        using var bgPaint = new SKPaint { IsAntialias = true };
        using var bgShader = SKShader.CreateLinearGradient(
            new SKPoint(badgeRect.Left, badgeRect.Top), new SKPoint(badgeRect.Right, badgeRect.Bottom),
            [new SKColor(0xDC, 0x26, 0x26), new SKColor(0x7F, 0x1D, 0x1D)],
            SKShaderTileMode.Clamp);
        bgPaint.Shader = bgShader;
        c.DrawRoundRect(new SKRoundRect(badgeRect, 13, 13), bgPaint);

        using var bordPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f,
            Color = new SKColor(0xFC, 0xD3, 0x4D, 200),
        };
        c.DrawRoundRect(new SKRoundRect(badgeRect, 13, 13), bordPaint);

        DrawText(c, text, badgeRect.MidX, badgeRect.MidY + 5f, SKColors.White, 13f, bold: true, hAlign: SKTextAlign.Center);
    }

    // ── Alt gradient overlay + fiyat bilgileri ────────────────────────────
    private static void DrawBottomOverlay(
        SKCanvas c,
        string title,
        decimal lowest,
        decimal? avg,
        decimal? bestPriceFrom,
        string currency,
        string? merchant,
        SKColor accent)
    {
        // Overlay yüksekliği içeriğe göre dinamik
        const float overlayH = 210f;
        const float overlayY = H - overlayH;

        var overlayRect = new SKRect(0, overlayY, W, H);
        using var rrect = new SKRoundRect();
        rrect.SetRectRadii(overlayRect,
        [
            new SKPoint(0, 0),
            new SKPoint(0, 0),
            new SKPoint(Radius, Radius),
            new SKPoint(Radius, Radius),
        ]);

        // Siyah gradient
        using var overlayPaint = new SKPaint { IsAntialias = true };
        using var overlayShader = SKShader.CreateLinearGradient(
            new SKPoint(0, overlayY), new SKPoint(0, H),
            [new SKColor(0x00, 0x00, 0x00, 0),
             new SKColor(0x00, 0x00, 0x00, 210),
             new SKColor(0x00, 0x00, 0x00, 245)],
            SKShaderTileMode.Clamp);
        overlayPaint.Shader = overlayShader;
        c.DrawRoundRect(rrect, overlayPaint);

        // Üst ince accent çizgisi
        using var accentLine = new SKPaint { IsAntialias = true };
        using var accentShader = SKShader.CreateLinearGradient(
            new SKPoint(20, overlayY + 1), new SKPoint(W - 20, overlayY + 1),
            [new SKColor(accent.Red, accent.Green, accent.Blue, 0),
             new SKColor(accent.Red, accent.Green, accent.Blue, 160),
             new SKColor(accent.Red, accent.Green, accent.Blue, 0)],
            SKShaderTileMode.Clamp);
        accentLine.Shader = accentShader;
        accentLine.StrokeWidth = 1.5f;
        c.DrawLine(20, overlayY + 1, W - 20, overlayY + 1, accentLine);

        const float padX = 20f;
        var cy = overlayY + 18f;

        // Marka rozeti + mağaza
        DrawText(c, "🐝 BeebakSana", padX, cy + 10f, new SKColor(accent.Red, accent.Green, accent.Blue), 11f, bold: true);
        if (!string.IsNullOrWhiteSpace(merchant))
        {
            DrawText(c, merchant, W - padX, cy + 10f, new SKColor(0xCB, 0xD5, 0xE1), 10f, hAlign: SKTextAlign.Right);
        }
        cy += 24f;

        // Ürün başlığı
        cy = DrawWrappedText(c, title, padX, cy, W - padX * 2f, 13f, SKColors.White, 2);
        cy += 10f;

        // Piyasa Fiyatı: üstü çizili yüksek fiyat (varsa)
        if (bestPriceFrom.HasValue && Math.Abs(bestPriceFrom.Value - lowest) >= 0.01m)
        {
            DrawText(c, "Piyasa Fiyatı", padX, cy + 10f, new SKColor(0x94, 0xA3, 0xB8), 10f);

            using var strikeTf = SKTypeface.FromFamilyName(null, SKFontStyle.Normal);
            using var strikeFont = new SKFont(strikeTf, 11f);
            using var strikePaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x94, 0xA3, 0xB8, 200) };
            var oldText = FormatMoney(bestPriceFrom.Value, currency);
            var oldW = strikeFont.MeasureText(oldText);
            c.DrawText(oldText, W - padX - oldW, cy + 10f, SKTextAlign.Left, strikeFont, strikePaint);
            using var linePaint = new SKPaint { Color = new SKColor(0x94, 0xA3, 0xB8, 200), StrokeWidth = 1.2f };
            c.DrawLine(W - padX - oldW, cy + 6f, W - padX, cy + 6f, linePaint);
            cy += 22f;
        }

        // En Ucuz — büyük, vurgulu
        var priceY = cy + 18f;
        DrawText(c, "En Ucuz", padX, priceY - 14f, new SKColor(0x6E, 0xE7, 0xB7), 10f, bold: true);

        DrawText(c, FormatMoney(lowest, currency), padX, priceY,
            new SKColor(accent.Red, accent.Green, accent.Blue), 26f, bold: true);
    }

    // ── Çapraz watermark ──────────────────────────────────────────────────
    private static void DrawWatermark(SKCanvas c)
    {
        const string text = "BeeBak Sana";
        const float size  = 22f;
        const byte  alpha = 38; // çok şeffaf — ürünü örtmez

        using var tf    = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        using var font  = new SKFont(tf, size);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = new SKColor(255, 255, 255, alpha),
        };

        c.Save();
        // Kartın merkezinden -35° döndür
        c.Translate(W * 0.5f, H * 0.5f);
        c.RotateDegrees(-35f);

        // İki satır: biraz üst alta yerleştir
        var textW = font.MeasureText(text);
        c.DrawText(text, -textW * 0.5f, -size * 0.6f, SKTextAlign.Left, font, paint);
        c.DrawText(text, -textW * 0.5f,  size * 1.3f, SKTextAlign.Left, font, paint);

        c.Restore();
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────
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

    private static float DrawWrappedText(
        SKCanvas c, string text, float x, float startY,
        float maxW, float size, SKColor color, int maxLines)
    {
        using var tf = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
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
                line = test;
            else
            {
                if (line.Length > 0) lines.Add(line);
                line = word;
            }
            if (lines.Count >= maxLines) break;
        }
        if (line.Length > 0 && lines.Count < maxLines) lines.Add(line);

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
