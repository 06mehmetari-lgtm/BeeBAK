using BeeBAK.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace BeeBAK.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class BeeBAKController : AbpControllerBase
{
    protected BeeBAKController()
    {
        LocalizationResource = typeof(BeeBAKResource);
    }
}
