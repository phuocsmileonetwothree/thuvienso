using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace thuvienso.Models
{
    public enum CancelReasonSource { admin, customer, auto }
    public enum OrderStatus { pending, paid, failed, canceled }

    /// <summary>
    /// Thực thể lưu trữ trạng thái thanh toán và cổng kiểm soát giao dịch trực tuyến
    /// </summary>
    [Table("OrderDetails")]
    public class OrderDetail
    {
        [Key]
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string OrderCode { get; set; } = string.Empty;

        public string? QrCodeUrl { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? PaymentLinkId { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal PercentPaid { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal PricePaid { get; set; }

        [Required]
        public OrderStatus Status { get; set; } = OrderStatus.pending;

        public DateTime? TransactedAt { get; set; }

        // Quản lý thời gian tự động qua AppDbContext
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public DateTime? CanceledAt { get; set; }
        public CancelReasonSource? CanceledBy { get; set; }
        public int? CanceledById { get; set; }

        public int OrderId { get; set; }

        [ForeignKey("CanceledById")]
        public virtual User? CanceledByUser { get; set; }

        [ForeignKey("OrderId")]
        public virtual Order? Order { get; set; }
    }
}
