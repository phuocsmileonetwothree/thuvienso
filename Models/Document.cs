using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể quản lý thông tin chi tiết của Tài liệu số
    /// </summary>
    public class Document
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? FileUrl { get; set; }

        public string? PreviewFileUrl { get; set; }

        [MaxLength(255)]
        public string? Thumb { get; set; }

        public int? CategoryId { get; set; }
        public int? PublisherId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Price { get; set; }

        public bool IsFree { get; set; } = true;
        public int? View { get; set; }
        public int? Download { get; set; }
        public int? Purchase { get; set; }

        public DateTime PublicDate { get; set; }
        public DateTime? ReprintDate { get; set; }

        // Quản lý thời gian tự động qua AppDbContext
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        [ForeignKey("CategoryId")]
        public virtual Category? Category { get; set; }

        [ForeignKey("PublisherId")]
        public virtual Publisher? Publisher { get; set; }

        public virtual ICollection<DocumentAuthor> DocumentAuthors { get; set; } = new List<DocumentAuthor>();
        public virtual ICollection<Order> Payments { get; set; } = new List<Order>();
        public virtual ICollection<Download> Downloads { get; set; } = new List<Download>();
        public virtual ICollection<QRCode> QRCodes { get; set; } = new List<QRCode>();
    }
}
