using System.Threading.Tasks;
using BeeBAK.Marketplaces.Cimri;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeeBAK.Controllers;

/// <summary>
/// Otomatik API (conventional controller) <see cref="ICimriProductAppService.ClearAllStoredDataAsync"/>
/// için swagger'da route oluşturmadığından, POST uç noktasını burada sabitliyoruz (405 önlenir).
/// </summary>
[Route("api/app/cimri-product")]
public class CimriProductCleanupController : BeeBAKController
{
    private readonly ICimriProductAppService _cimriProductAppService;

    public CimriProductCleanupController(ICimriProductAppService cimriProductAppService)
    {
        _cimriProductAppService = cimriProductAppService;
    }

    [HttpPost("clear-all-stored-data")]
    [Authorize(BeeBAKPermissions.Cimri.Sync)]
    public virtual Task ClearAllStoredDataAsync()
    {
        return _cimriProductAppService.ClearAllStoredDataAsync();
    }
}
