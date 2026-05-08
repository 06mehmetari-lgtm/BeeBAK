namespace BeeBAK.Permissions;

public static class BeeBAKPermissions
{
    public const string GroupName = "BeeBAK";

    public static class Trendyol
    {
        public const string Default = GroupName + ".Trendyol";
        public const string Sync = Default + ".Sync";
    }

    public static class Hepsiburada
    {
        public const string Default = GroupName + ".Hepsiburada";
    }
}
