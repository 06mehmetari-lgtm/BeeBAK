using System.Threading.Tasks;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.Identity;

namespace BeeBAK.Identity;

/// <summary>
/// <c>admin</c> rolüne uygulamada kayıtlı tüm izinleri verir (menüler dahil).
/// Yeni permission tanımlandığında migrator/host seed tekrar çalışınca güncellenir.
/// </summary>
public class AdminRoleFullPermissionDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    /// <summary>Identity rol ve kullanıcı tohumundan sonra çalışsın.</summary>
    public int Order => 100;

    private readonly IPermissionManager _permissionManager;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;

    public AdminRoleFullPermissionDataSeedContributor(
        IPermissionManager permissionManager,
        IPermissionDefinitionManager permissionDefinitionManager)
    {
        _permissionManager = permissionManager;
        _permissionDefinitionManager = permissionDefinitionManager;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        const string adminRoleName = "admin";

        var permissions = await _permissionDefinitionManager.GetPermissionsAsync();

        foreach (var permission in permissions)
        {
            // Bazı izinler yalnızca kullanıcı/provider bazlıdır (ör. AbpIdentity.UserLookup); role atanamaz.
            if (permission.Providers.Count > 0 &&
                !permission.Providers.Contains(RolePermissionValueProvider.ProviderName))
            {
                continue;
            }

            await _permissionManager.SetForRoleAsync(adminRoleName, permission.Name, true);
        }
    }
}
