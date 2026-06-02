using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace thuvienso.Models
{
    public enum QRCodeType { view, download }

    /// <summary>
    /// Thực thể quản lý và theo dõi thông tin tương tác qua mã QR tĩnh/động của tài liệu
    /// </summary>
    public class QRCode
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int DocumentId { get; set; }

        [Required]
        public QRCodeType Type { get; set; }

        [StringLength(500)]
        public string? QrUrl { get; set; }

        // Bộ đếm tổng số lượt quét phục vụ thống kê
        public int ScanCount { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; } = null!;
    }

    public static class QRCodeFileName
    {
        public const string View = "qr-view.png";
        public const string Download = "qr-download.png";
    }
}
