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

        myGroup.AddPermission(BeeBAKPermissions.Hepsiburada.Default, L("Permission:Hepsiburada"));

        var cimri = myGroup.AddPermission(BeeBAKPermissions.Cimri.Default, L("Permission:Cimri"));
        cimri.AddChild(BeeBAKPermissions.Cimri.Sync, L("Permission:Cimri.Sync"));
        cimri.AddChild(BeeBAKPermissions.Cimri.Probe, L("Permission:Cimri.Probe"));

        var akakce = myGroup.AddPermission(BeeBAKPermissions.Akakce.Default, L("Permission:Akakce"));
        akakce.AddChild(BeeBAKPermissions.Akakce.Sync, L("Permission:Akakce.Sync"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<BeeBAKResource>(name);
    }
}
