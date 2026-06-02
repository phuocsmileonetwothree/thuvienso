using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace thuvienso.Models
{
    /// <summary>
    /// Thực thể quản lý thông tin giao dịch đặt mua tài liệu
    /// </summary>
    [Table("Orders")]
    public class Order
    {
        [Key]
        public int Id { get; set; }

        [Column(TypeName = "DECIMAL(10,2)")]
        public decimal TotalPrice { get; set; }

        // Quản lý thời gian tự động qua AppDbContext
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int UserId { get; set; }
        public int DocumentId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [ForeignKey("DocumentId")]
        public virtual Document? Document { get; set; }

        public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    }
}
