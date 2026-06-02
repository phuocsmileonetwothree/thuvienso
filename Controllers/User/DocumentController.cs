using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using thuvienso.Models;
using thuvienso.Repositories;
using thuvienso.Services;

namespace thuvienso.Controllers.User
{
    [Route("document")]
    public class DocumentController : Controller
    {
        private readonly DocumentRepository _documentRepo;
        private readonly DocumentService _documentService;
        private readonly OrderService _orderService;

        public DocumentController(
            DocumentRepository documentRepo,
            DocumentService documentService,
            OrderService orderService)
        {
            _documentRepo = documentRepo;
            _documentService = documentService;
            _orderService = orderService;
        }

        /// <summary>
        /// Hiển thị trang chi tiết tài liệu.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> Details(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");

            // Gọi Service lấy thông tin tài liệu và tăng lượt View tự động
            var doc = await _documentService.GetDocumentDetailAsync(id);
            if (doc == null) return NotFound();

            // Đổ dữ liệu Sidebar & Danh sách liên quan thông qua Repository
            ViewBag.NewestDocs = await _documentRepo.GetNewestDocsAsync(id, 12);
            ViewBag.PopularDocs = await _documentRepo.GetPopularDocsAsync(id, 12);
            ViewBag.AllCategories = await _documentRepo.GetAllCategoriesAsync();

            decimal maxPercentPaid = 0;
            string fileToRender = doc.IsFree ? doc.FileUrl : doc.PreviewFileUrl;

            if (userId != null)
            {
                maxPercentPaid = await _documentService.GetMaxPercentPaidAsync(userId.Value, doc.Id);

                // Tính toán giá lũy tiến từ Service và gán thẳng vào ViewBag
                var pricing = _documentService.CalculatePricing(doc, maxPercentPaid);
                ViewBag.PercentPaid = maxPercentPaid;
                ViewBag.Price25 = pricing["Price25"];
                ViewBag.Price50 = pricing["Price50"];
                ViewBag.Price100 = pricing["Price100"];

                if (!doc.IsFree && maxPercentPaid > 0)
                {
                    var fileName = Path.GetFileName(doc.FileUrl);
                    var folderPath = Path.GetDirectoryName(doc.FileUrl);
                    if (maxPercentPaid >= 100)
                    {
                        fileToRender = doc.FileUrl;
                    }
                    else if (maxPercentPaid >= 50)
                    {
                        fileToRender = $"{folderPath}/50_{fileName}";
                    }
                    else if (maxPercentPaid >= 25)
                    {
                        fileToRender = $"{folderPath}/25_{fileName}";
                    }
                    //else
                    //{
                    //    fileToRender = $"/payment/user-{userId}/document-{doc.Id}/{fileName}";
                    //}
                }
            }

            ViewBag.FileToRender = fileToRender;
            return View("~/Views/User/Document/Detail.cshtml", doc);
        }

        /// <summary>
        /// Xử lý tải tệp tin tài liệu.
        /// </summary>
        [HttpGet("download/{id}")]
        public async Task<IActionResult> Download(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Redirect("/user/auth/login");

            // Hứng cả StatusCode lẫn ErrorMessage về
            var (filePath, fileName, statusCode, errorMessage) = await _documentService.ProcessDownloadAsync(id, userId.Value);

            // Nếu có mã lỗi, dùng hàm StatusCode có sẵn của Controller để trả về đúng chuẩn HTTP kèm message tiếng Việt
            if (statusCode.HasValue)
            {
                return StatusCode(statusCode.Value, errorMessage);
            }

            var contentType = "application/octet-stream";
            return PhysicalFile(filePath!, contentType, fileName);
        }

        /// <summary>
        /// Tăng số lượt quét mã QR xem tài liệu và điều hướng.
        /// </summary>
        [HttpGet("qr-detail/{id}")]
        public async Task<IActionResult> QrDetail(int id)
        {
            var success = await _documentService.IncrementQrScanCountAsync(id, QRCodeType.view);
            if (!success)
                return NotFound("Mã QR không hợp lệ hoặc đã bị vô hiệu hóa.");

            return Redirect($"/document/{id}");
        }

        /// <summary>
        /// Tăng số lượt quét mã QR tải tài liệu và điều hướng.
        /// </summary>
        [HttpGet("qr-download/{id}")]
        public async Task<IActionResult> QrDownload(int id)
        {
            var success = await _documentService.IncrementQrScanCountAsync(id, QRCodeType.download);
            if (!success)
                return NotFound("Mã QR không hợp lệ hoặc đã bị vô hiệu hóa.");

            return Redirect($"/document/download/{id}");
        }
    }
}








//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using thuvienso.Data;
//using thuvienso.Models;
//using thuvienso.Services;

//namespace thuvienso.Controllers.User
//{
//    [Route("document")]
//    public class DocumentController : Controller
//    {
//        private readonly AppDbContext _context;
//        private readonly OrderService _orderService;

//        public DocumentController(AppDbContext context, OrderService orderService)
//        {
//            _context = context;
//            _orderService = orderService;
//        }

//        [HttpGet("{id}")]
//        public async Task<IActionResult> Details(int id,
//            [FromQuery] string? status,
//            [FromQuery] string? orderCode,
//            [FromQuery] bool? cancel)
//        {
//            if (cancel == true && !string.IsNullOrEmpty(orderCode))
//            {
//                await _orderService.CompleteOrderAsync(orderCode, "canceled");
//                TempData["StatusMessage"] = "Đơn hàng của bạn đã được hủy thành công.";
//            }

//            var userId = HttpContext.Session.GetInt32("UserId");
//            var doc = await _context.Documents
//                .Include(d => d.Category)
//                .Include(d => d.Publisher)
//                .Include(d => d.DocumentAuthors)
//                    .ThenInclude(da => da.Author)
//                .FirstOrDefaultAsync(d => d.Id == id);

//            if (doc == null) return NotFound();

//            doc.View = (doc.View ?? 0) + 1;
//            await _context.SaveChangesAsync();

//            ViewBag.NewestDocs = await _context.Documents
//                .Where(d => d.Id != id)
//                .Include(d => d.DocumentAuthors).ThenInclude(da => da.Author)
//                .OrderByDescending(d => d.Id).Take(12).ToListAsync();

//            ViewBag.PopularDocs = await _context.Documents
//                .Where(d => d.Id != id)
//                .Include(d => d.DocumentAuthors).ThenInclude(da => da.Author)
//                .OrderByDescending(d => d.View).Take(12).ToListAsync();

//            ViewBag.AllCategories = await _context.Categories.ToListAsync();

//            decimal maxPercentPaid = 0;

//            if (userId != null)
//            {
//                // Tìm xem người dùng đã từng có giao dịch "paid" nào cho tài liệu này chưa và lấy phần trăm cao nhất
//                maxPercentPaid = await _context.OrderDetails
//                    .Where(d => d.Order.UserId == userId && d.Order.DocumentId == doc.Id && d.Status == "paid")
//                    .Select(d => (decimal?)d.PercentPaid)
//                    .MaxAsync() ?? 0;

//                await SetupPaymentPricing(userId.Value, doc, maxPercentPaid);
//            }

//            string fileToRender = doc.IsFree ? doc.FileUrl : doc.PreviewFileUrl;

//            if (!doc.IsFree && userId != null && maxPercentPaid > 0)
//            {
//                var fileName = Path.GetFileName(doc.FileUrl);
//                if (maxPercentPaid >= 100)
//                {
//                    fileToRender = doc.FileUrl;
//                }
//                else
//                {
//                    fileToRender = $"/payment/user-{userId}/document-{doc.Id}/{fileName}";
//                }

//                ViewBag.PercentPaid = maxPercentPaid;
//            }

//            ViewBag.FileToRender = fileToRender;
//            return View("~/Views/User/Document/Detail.cshtml", doc);
//        }

//        [HttpGet("download/{id}")]
//        public async Task<IActionResult> Download(int id)
//        {
//            var userId = HttpContext.Session.GetInt32("UserId");
//            if (userId == null)
//                return Redirect("/user/auth/login");

//            var doc = await _context.Documents.FindAsync(id);
//            if (doc == null || string.IsNullOrEmpty(doc.FileUrl))
//                return NotFound();

//            var fileName = Path.GetFileName(doc.FileUrl);
//            string filePath;

//            if (doc.IsFree)
//            {
//                filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", doc.FileUrl.TrimStart('/'));
//            }
//            else
//            {
//                // Lấy phần trăm đã thanh toán cao nhất từ các giao dịch thành công (paid)
//                decimal maxPercentPaid = await _context.OrderDetails
//                    .Where(d => d.Order.UserId == userId && d.Order.DocumentId == doc.Id && d.Status == "paid")
//                    .Select(d => (decimal?)d.PercentPaid)
//                    .MaxAsync() ?? 0;

//                if (maxPercentPaid == 0)
//                {
//                    return Unauthorized("❌ Bạn chưa thanh toán cho tài liệu này.");
//                }

//                if (maxPercentPaid >= 100)
//                {
//                    filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", doc.FileUrl.TrimStart('/'));
//                }
//                else
//                {
//                    filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "payment", $"user-{userId}", $"document-{doc.Id}", fileName);
//                    if (!System.IO.File.Exists(filePath))
//                        return NotFound("❌ File preview không tồn tại. Vui lòng liên hệ hỗ trợ.");
//                }
//            }

//            if (!System.IO.File.Exists(filePath))
//                return NotFound("❌ Không tìm thấy file.");

//            doc.Download = (doc.Download ?? 0) + 1;
//            await _context.SaveChangesAsync();

//            var contentType = "application/octet-stream";
//            return PhysicalFile(filePath, contentType, fileName);
//        }

//        [HttpGet("qr-detail/{id}")]
//        public async Task<IActionResult> QrDetail(int id)
//        {
//            var qr = await _context.QRCodes
//                .FirstOrDefaultAsync(q => q.DocumentId == id && q.Type == QRCodeType.view && q.IsActive);

//            if (qr == null)
//                return NotFound("❌ Mã QR không hợp lệ hoặc đã bị vô hiệu hóa.");

//            qr.ScanCount += 1;
//            await _context.SaveChangesAsync();

//            return Redirect($"/document/{id}");
//        }

//        [HttpGet("qr-download/{id}")]
//        public async Task<IActionResult> QrDownload(int id)
//        {
//            var qr = await _context.QRCodes
//                .FirstOrDefaultAsync(q => q.DocumentId == id && q.Type == QRCodeType.download && q.IsActive);

//            if (qr == null)
//                return NotFound("❌ Mã QR không hợp lệ hoặc đã bị vô hiệu hóa.");

//            qr.ScanCount += 1;
//            await _context.SaveChangesAsync();

//            return Redirect($"/document/download/{id}");
//        }

//        private async Task SetupPaymentPricing(int userId, Document doc, decimal currentPaidPercent)
//        {
//            decimal basePrice = doc.Price ?? 0;
//            if (basePrice < 0) basePrice = 0;

//            int CalculatePrice(int targetPercent)
//            {
//                decimal addedPercent = Math.Max(0, (decimal)targetPercent - currentPaidPercent);
//                decimal result = basePrice * (addedPercent / 100m);
//                return (int)Math.Round(result, MidpointRounding.AwayFromZero);
//            }

//            ViewBag.PercentPaid = currentPaidPercent;
//            ViewBag.Price25 = CalculatePrice(25);
//            ViewBag.Price50 = CalculatePrice(50);
//            ViewBag.Price100 = CalculatePrice(100);
//        }
//    }
//}
