namespace VpnNotes.Core.Models
{
    public enum UserRole
    {
        User = 0,
        Stats = 1,
        Admin = 2
    }

    public static class UserRoleExtensions
    {
        public static string ToDbString(this UserRole role)
        {
            switch (role)
            {
                case UserRole.User: return "user";
                case UserRole.Stats: return "stats";
                case UserRole.Admin: return "admin";
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        public static UserRole ParseDbString(string value)
        {
            switch (value.ToLower())
            {
                case "user": return UserRole.User;
                case "stats": return UserRole.Stats;
                case "admin": return UserRole.Admin;
                default: throw new ArgumentException($"Unknown role: {value}");
            }
        }

        public static string GetPgGroupRole(this UserRole role)
        {
            switch (role)
            {
                case UserRole.User: return "vpnnotes_user";
                case UserRole.Stats: return "vpnnotes_stats";
                case UserRole.Admin: return "vpnnotes_admin";
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }
    }
}