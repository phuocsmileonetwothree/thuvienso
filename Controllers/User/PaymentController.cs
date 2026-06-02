using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using thuvienso.Services;

namespace thuvienso.Controllers
{
    [Route("payment")]
    public class PaymentController : Controller
    {
        private readonly PaymentService _paymentService;

        public PaymentController(PaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        /// <summary>
        /// Tiếp nhận yêu cầu tạo Link/QR thanh toán từ phía Client
        /// </summary>
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromForm] int documentId, [FromForm] int percent)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();

            var result = await _paymentService.CreatePaymentLinkAsync(userId.Value, documentId, percent);
            if (result == null) return BadRequest("Không thể khởi tạo link thanh toán.");

            return Json(new
            {
                success = true,
                data = result
            });
        }

        /// <summary>
        /// Tiếp nhận yêu cầu hủy đơn hàng chủ động từ khách hàng
        /// </summary>
        [HttpPost("cancel")]
        public async Task<IActionResult> Cancel([FromForm] string orderCode)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();
            bool isCancelled = await _paymentService.CancelPaymentAsync(userId.Value, orderCode);
            if (isCancelled)
            {
                return Ok(new { success = true, message = "Đơn hàng đã được hủy thành công." });
            }

            return BadRequest("Không thể hủy đơn hàng này hoặc giao dịch không tồn tại.");
        }

        /// <summary>
        /// Tiếp nhận Webhook phản hồi kết quả giao dịch tự động từ bên phía PayOS
        /// </summary>
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook()
        {
            try
            {
                // Cho phép đọc lại Body stream nhiều lần mà không bị lỗi mất dấu luồng
                Request.EnableBuffering();
                using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
                var rawBody = await reader.ReadToEndAsync();
                Request.Body.Position = 0; // Reset lại vị trí con trỏ stream về đầu

                // 1. Phân rã nhanh để kiểm tra hoặc xác nhận webhook mẫu kiểm thử từ PayOS
                var payload = JObject.Parse(rawBody);
                var eventData = payload["data"] as JObject;
                var receivedSignature = payload["signature"]?.ToString() ?? string.Empty;

                if (eventData == null || eventData["orderCode"]?.ToString() == "123")
                {
                    return Ok(new { code = "00", message = "Confirm webhook successfully" });
                }

                // 2. Đẩy toàn bộ dữ liệu chuỗi thô và chữ ký xuống Service tự xử lý
                // Luồng xử lý ngầm (Background Task) cho file, mail, và SignalR đã nằm gọn bên trong Service
                bool processed = await _paymentService.ProcessWebhookAsync(rawBody, receivedSignature);
                if (!processed)
                {
                    return BadRequest("Xử lý thông tin Webhook thất bại hoặc chữ ký không hợp lệ.");
                }

                // Trả về HTTP 200 thật nhanh để phía PayOS không bắn lại Webhook nhiều lần
                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Webhook Controller Error]: {ex.Message}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Lỗi hệ thống khi xử lý Webhook.");
            }
        }
    }
}
