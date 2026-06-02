using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể quản lý thông tin Tác giả
    /// </summary>
    public class Author
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        // Danh sách liên kết biểu thị quan hệ nhiều-nhiều với thực thể Tài liệu (Document)
        public virtual ICollection<DocumentAuthor> DocumentAuthors { get; set; } = new List<DocumentAuthor>();
    }
}
