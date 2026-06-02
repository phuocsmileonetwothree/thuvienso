using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể quản lý Danh mục tài liệu (Hỗ trợ cấu trúc cây phân cấp Đệ quy)
    /// </summary>
    public class Category
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        // Khóa ngoại liên kết danh mục cha (Null nếu là danh mục gốc)
        public int? ParentId { get; set; }

        [ForeignKey("ParentId")]
        public virtual Category? Parent { get; set; }

        // Danh sách các danh mục con trực thuộc
        public virtual ICollection<Category> Children { get; set; } = new List<Category>();

        // Danh sách tài liệu thuộc danh mục này
        public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
