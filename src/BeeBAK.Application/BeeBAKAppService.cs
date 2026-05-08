using BeeBAK.Localization;
using Volo.Abp.Application.Services;

namespace BeeBAK;

/* Inherit your application services from this class.
 */
public abstract class BeeBAKAppService : ApplicationService
{
    protected BeeBAKAppService()
    {
        LocalizationResource = typeof(BeeBAKResource);
    }
}
