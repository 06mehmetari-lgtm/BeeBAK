using System.Threading.Tasks;
using BeeBAK.Marketplaces.Akakce;
using BeeBAK.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeeBAK.Controllers;

/// <summary>
/// Conventional controller does not expose this POST route reliably, so keep it explicit.
/// </summary>
[Route("api/app/akakce-product")]
public class AkakceProductCleanupController : BeeBAKController
{
    private readonly IAkakceProductAppService _akakceProductAppService;

    public AkakceProductCleanupController(IAkakceProductAppService akakceProductAppService)
    {
        _akakceProductAppService = akakceProductAppService;
    }

    [HttpPost("clear-all-stored-data")]
    [Authorize(BeeBAKPermissions.Akakce.Sync)]
    public virtual Task ClearAllStoredDataAsync()
    {
        return _akakceProductAppService.ClearAllStoredDataAsync();
    }
}
