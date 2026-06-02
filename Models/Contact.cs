using System;
using System.ComponentModel.DataAnnotations;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể lưu trữ thông tin phản hồi, liên hệ từ người dùng
    /// </summary>
    public class Contact
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Name { get; set; } = null!;

        [Required, MaxLength(255), EmailAddress]
        public string Email { get; set; } = null!;

        [Required, MaxLength(12)]
        public string Phone { get; set; } = null!;

        [Required, MaxLength(255)]
        public string Subject { get; set; } = null!;

        [Required]
        public string Message { get; set; } = null!;

        // Trạng thái xử lý yêu cầu liên hệ từ phía Quản trị viên
        public bool IsHandled { get; set; } = false;

        // Thời gian tiếp nhận yêu cầu liên hệ
        public DateTime CreatedAt { get; set; }
    }
}
