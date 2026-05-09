namespace BeeBAK.Permissions;

public static class BeeBAKPermissions
{
    public const string GroupName = "BeeBAK";

    public static class Hepsiburada
    {
        public const string Default = GroupName + ".Hepsiburada";
    }

    public static class Cimri
    {
        public const string Default = GroupName + ".Cimri";

        public const string Sync = Default + ".Sync";

        public const string Probe = Default + ".Probe";
    }
}
