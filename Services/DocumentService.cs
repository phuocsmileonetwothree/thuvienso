using PdfSharpCore.Pdf.Filters;
using PdfSharpCore.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using thuvienso.Models;
using thuvienso.Repositories;

namespace thuvienso.Services
{
    public class DocumentService
    {
        private readonly DocumentRepository _documentRepo;
        private readonly OrderRepository _orderRepo; 

        public DocumentService(
            DocumentRepository documentRepo,
            OrderRepository orderRepo)
        {
            _documentRepo = documentRepo;
            _orderRepo = orderRepo;
        }

        /// <summary>
        /// Lấy chi tiết tài liệu và tăng lượt xem.
        /// </summary>
        public async Task<Document?> GetDocumentDetailAsync(int id)
        {
            var doc = await _documentRepo.FindByIdAsync(id);
            if (doc == null) return null;

            doc.View = (doc.View ?? 0) + 1;
            _documentRepo.Update(doc);
            await _documentRepo.SaveAsync();

            return doc;
        }

        /// <summary>
        /// Lấy phần trăm đã thanh toán mới nhất.
        /// </summary>
        public async Task<decimal> GetMaxPercentPaidAsync(int userId, int documentId)
        {
            var latestPaidDetail = await _orderRepo.FindLatestPaidDetailByPairAsync(userId, documentId);
            // Nếu tìm thấy bản ghi đã thanh toán, trả về số phần trăm lũy tiến của bản ghi đó.
            // Nếu không có bản ghi nào (User chưa từng mua), trả về 0.
            return latestPaidDetail?.PercentPaid ?? 0;
        }

        /// <summary>
        /// Tính giá lũy tiến của tài liệu.
        /// </summary>
        public Dictionary<string, int> CalculatePricing(Document doc, decimal currentPaidPercent)
        {
            decimal basePrice = doc.Price ?? 0;
            if (basePrice < 0) basePrice = 0;

            int Calculate(int targetPercent)
            {
                decimal addedPercent = Math.Max(0, (decimal)targetPercent - currentPaidPercent);
                decimal result = basePrice * (addedPercent / 100m);
                return (int)Math.Round(result, MidpointRounding.AwayFromZero);
            }
            return new Dictionary<string, int>
            {
                { "Price25", Calculate(25) },
                { "Price50", Calculate(50) },
                { "Price100", Calculate(100) }
            };
        }

        /// <summary>
        /// Xử lý tải tệp tài liệu và kiểm tra quyền truy cập.
        /// </summary>
        public async Task<(string? FilePath, string? FileName, int? StatusCode, string? ErrorMessage)> ProcessDownloadAsync(int id, int userId)
        {
            var doc = await _documentRepo.FindByIdAsync(id);
            if (doc == null || string.IsNullOrEmpty(doc.FileUrl))
                return (null, null, 404, "Không tìm thấy tài liệu.");

            var fileName = Path.GetFileName(doc.FileUrl);

            // Xác định thư mục chứa file (thư mục cha của file gốc)
            // Cắt bỏ phần domain để lấy đường dẫn tương đối, sau đó lấy đường dẫn thư mục
            var docPathRelative = doc.FileUrl.TrimStart('/');
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", Path.GetDirectoryName(docPathRelative)!);

            string filePath;

            if (doc.IsFree)
            {
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", doc.FileUrl.TrimStart('/'));
            }
            else
            {
                decimal maxPercentPaid = await GetMaxPercentPaidAsync(userId, doc.Id);

                if (maxPercentPaid == 0)
                {
                    // Nếu chưa mua cho phép tải bản Preview
                    if (!string.IsNullOrEmpty(doc.PreviewFileUrl))
                    {
                        filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", doc.PreviewFileUrl.TrimStart('/'));
                        fileName = Path.GetFileName(doc.PreviewFileUrl);
                    }
                    else
                    {
                        return (null, null, 400, "Tài liệu này không có bản xem trước. Vui lòng thanh toán để tải về.");
                    }
                }
                else if (maxPercentPaid >= 100)
                {
                    filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", doc.FileUrl.TrimStart('/'));
                }
                else
                {
                    // Trỏ tới file 25_ hoặc 50_ nằm chung folder với file gốc
                    string partialFileName = $"{(int)maxPercentPaid}_{fileName}";
                    filePath = Path.Combine(folder, partialFileName);
                }
            }
            // Kiểm tra file tồn tại
            if (!File.Exists(filePath))
                return (null, null, 404, "Không tìm thấy file tài liệu hoặc file bị lỗi.");

            // Tăng lượt tải
            doc.Download = (doc.Download ?? 0) + 1;
            _documentRepo.Update(doc);
            await _documentRepo.SaveAsync();

            return (filePath, fileName, null, null);
        }

        /// <summary>
        /// Tăng số lượt quét mã QR.
        /// </summary>
        public async Task<bool> IncrementQrScanCountAsync(int documentId, QRCodeType type)
        {
            var qr = await _documentRepo.FindQrCodeAsync(documentId, type);
            if (qr == null) return false;

            qr.ScanCount += 1;
            await _documentRepo.SaveAsync();
            return true;
        }
    }
}
