using Microsoft.Extensions.Localization;
using BeeBAK.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace BeeBAK;

[Dependency(ReplaceServices = true)]
public class BeeBAKBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<BeeBAKResource> _localizer;

    public BeeBAKBrandingProvider(IStringLocalizer<BeeBAKResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
