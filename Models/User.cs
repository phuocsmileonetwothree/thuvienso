using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace thuvienso.Models
{
    public enum UserRole { admin, user }

    /// <summary>
    /// Thực thể quản lý tài khoản người dùng và phân quyền hệ thống
    /// </summary>
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(255), EmailAddress]
        public string Email { get; set; } = string.Empty;

        [MaxLength(12)]
        public string? Phone { get; set; }

        [Required, MaxLength(255)]
        public string Password { get; set; } = string.Empty;

        [MaxLength(6)]
        public string? ResetCode { get; set; }

        public DateTime? ResetCodeExpiry { get; set; }

        [Required]
        public UserRole Role { get; set; } = UserRole.user;

        // Quản lý thời gian tự động qua AppDbContext
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public virtual ICollection<Order> Payments { get; set; } = new List<Order>();
        public virtual ICollection<Download> Downloads { get; set; } = new List<Download>();
    }
}
