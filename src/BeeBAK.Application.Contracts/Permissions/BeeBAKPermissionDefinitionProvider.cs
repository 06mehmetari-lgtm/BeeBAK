using BeeBAK.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace BeeBAK.Permissions;

public class BeeBAKPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(BeeBAKPermissions.GroupName);

        var trendyol = myGroup.AddPermission(BeeBAKPermissions.Trendyol.Default, L("Permission:Trendyol"));
        trendyol.AddChild(BeeBAKPermissions.Trendyol.Sync, L("Permission:Trendyol.Sync"));
        trendyol.AddChild(BeeBAKPermissions.Trendyol.SyncNavigationCatalog, L("Permission:Trendyol.SyncNavigationCatalog"));

        myGroup.AddPermission(BeeBAKPermissions.Hepsiburada.Default, L("Permission:Hepsiburada"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<BeeBAKResource>(name);
    }
}
