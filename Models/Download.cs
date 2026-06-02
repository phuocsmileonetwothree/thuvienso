using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể lưu vết lịch sử tải tài liệu của người dùng
    /// </summary>
    public class Download
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public int DocumentId { get; set; }

        // Ghi nhận tức thời thời điểm thực hiện hành động tải xuống
        public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; } = null!;
    }
}
