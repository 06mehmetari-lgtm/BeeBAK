using Volo.Abp.Settings;

namespace BeeBAK.Settings;

public class BeeBAKSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(BeeBAKSettings.MySetting1));
    }
}
