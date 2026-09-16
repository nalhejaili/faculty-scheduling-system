using System;

namespace TrainerScheduler.Data.Entities
{
    /// <summary>
    /// Application user stored in SQLite.
    /// </summary>
    public sealed class UserEntity
    {
        public int Id { get; set; }

        public string Username { get; set; } = "";

        /// <summary>
        /// Base64-encoded PBKDF2 hash.
        /// </summary>
        public string PasswordHash { get; set; } = "";

        /// <summary>
        /// Base64-encoded salt.
        /// </summary>
        public string PasswordSalt { get; set; } = "";

        /// <summary>
        /// "Admin" or "Supervisor".
        /// </summary>
        public string Role { get; set; } = "Admin";

        /// <summary>
        /// For Supervisors, this is the department they can access. Null for Admin.
        /// </summary>
        public int? DepartmentId { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
