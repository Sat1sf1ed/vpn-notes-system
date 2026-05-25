namespace VpnNotes.Core.Models
{
    public class User
    {
        public string Username { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public string? FullName { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
    }
}
