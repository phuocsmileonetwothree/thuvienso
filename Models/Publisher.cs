using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể quản lý thông tin Nhà xuất bản / Đơn vị phát hành tài liệu
    /// </summary>
    public class Publisher
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
