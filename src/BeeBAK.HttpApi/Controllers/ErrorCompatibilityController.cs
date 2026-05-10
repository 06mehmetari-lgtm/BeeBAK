using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeeBAK.Controllers;

/// <summary>
/// ABP Angular + LeptonX teması, hata ekranı içeriği için bazen API sunucusunda
/// <c>GET /Error?httpStatusCode=...</c> (MVC çağrısına benzer) ister. Saf API host
/// projesinde bu rota tanımlı değilse istemci 404 alır. Burada minimal HTML döndürüyoruz;
/// asıl hata sunucu JSON’u / HTTP durumu üzerinden zaten gelir.
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class ErrorCompatibilityController : ControllerBase
{
    [HttpGet("/Error")]
    public IActionResult Get([FromQuery] int httpStatusCode = 500)
    {
        // İstek kendisi 200 olsun; tema semantiği için kod query string üzerindedir.
        var html =
            $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/></head><body data-http-status=\"{httpStatusCode}\"></body></html>";
        return Content(html, "text/html");
    }
}
