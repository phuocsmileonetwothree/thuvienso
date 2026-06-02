using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể trung gian liên kết quan hệ Nhiều-Nhiều giữa Tài liệu và Tác giả
    /// </summary>
    public class DocumentAuthor
    {
        [Key]
        public int Id { get; set; }

        public int DocumentId { get; set; }
        public int AuthorId { get; set; }

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; } = null!;

        [ForeignKey("AuthorId")]
        public virtual Author Author { get; set; } = null!;
    }
}
