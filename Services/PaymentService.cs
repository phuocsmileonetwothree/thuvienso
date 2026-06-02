using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using thuvienso.DTOs;
using thuvienso.Hubs;
using thuvienso.Interfaces;
using thuvienso.Repositories;
using thuvienso.Services.PaymentGateway;
using thuvienso.Models;
using Microsoft.Extensions.Configuration;
using Hangfire;

namespace thuvienso.Services
{
    public class PaymentService
    {
        private readonly OrderService _orderService;
        private readonly MailService _mailService;
        private readonly IPaymentGateway _paymentGateway;
        private readonly IHubContext<PaymentHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OrderRepository _orderRepo;
        private readonly IConfiguration _config;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public PaymentService(
            OrderService orderService,
            MailService mailService,
            IPaymentGateway paymentGateway,
            IHubContext<PaymentHub> hubContext,
            IServiceScopeFactory scopeFactory,
            OrderRepository orderRepo,
            IConfiguration config,
            IBackgroundJobClient backgroundJobClient)
        {
            _orderService = orderService;
            _mailService = mailService;
            _paymentGateway = paymentGateway;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _orderRepo = orderRepo;
            _config = config;
            _backgroundJobClient = backgroundJobClient;
        }

        /// <summary>
        /// Khởi tạo hoặc tái sử dụng liên kết thanh toán từ cổng kết nối ngoại vi.
        /// </summary>
        public async Task<object?> CreatePaymentLinkAsync(int userId, int documentId, int percent)
        {
            var baseUrl = _config["PublicUrl"];
            var cancelUrl = $"{baseUrl}/document/{documentId}";
            var returnUrl = $"{baseUrl}/user/profile";

            int percentValue = Math.Clamp(percent, 1, 100);

            var oldPendingDetail = await _orderRepo.FindLatestDetailByPairAsync(userId, documentId);

            if (oldPendingDetail != null && oldPendingDetail.Status == OrderStatus.pending)
            {
                if (oldPendingDetail.PercentPaid == percentValue &&
                    (DateTime.UtcNow - oldPendingDetail.CreatedAt <= TimeSpan.FromMinutes(15)))
                {
                    return new
                    {
                        orderCode = long.Parse(oldPendingDetail.OrderCode),
                        amount = (int)oldPendingDetail.PricePaid,
                        description = $"TL{documentId}M{percentValue}",
                        qrCode = oldPendingDetail.QrCodeUrl,
                        checkoutUrl = oldPendingDetail.CheckoutUrl,
                        paymentLinkId = oldPendingDetail.PaymentLinkId
                    };
                }

                if (!string.IsNullOrEmpty(oldPendingDetail.PaymentLinkId))
                {
                    try
                    {
                        string? actualGatewayStatus = await _paymentGateway.GetPaymentStatusAsync(oldPendingDetail.PaymentLinkId);

                        if (actualGatewayStatus == "PAID")
                        {
                            var fulfillment = await _orderService.CompleteOrderAsync(oldPendingDetail.OrderCode, OrderStatus.paid);
                            if (fulfillment != null && fulfillment.UserId != 0)
                            {
                                _executeBackgroundFulfillment(fulfillment);
                                await _contextHubNotify(fulfillment.UserId, fulfillment.DocumentId, OrderStatus.paid);
                            }
                            return new { isPaidAlready = true, message = "Đơn hàng trước đó của bạn đã được thanh toán thành công!" };
                        }

                        if (actualGatewayStatus == "PENDING" || actualGatewayStatus == "EXPIRED")
                        {
                            await _paymentGateway.CancelPaymentAsync(oldPendingDetail.PaymentLinkId, "Change My Mind");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PayOS Safety Clean-up Exception]: {ex.Message}. Bỏ qua để tạo đơn mới.");
                    }
                }

                await _orderService.CompleteOrderAsync(oldPendingDetail.OrderCode, OrderStatus.canceled, canceledById: null);
            }

            var newOrderDto = await _orderService.CreateOrderAsync(userId, documentId, percentValue);
            if (newOrderDto == null) return null;

            var paymentResult = await _paymentGateway.CreatePaymentUrlAsync(new CreateOrderPaymentGatewayRequest
            {
                OrderCode = newOrderDto.OrderCode,
                Amount = newOrderDto.Amount,
                Description = newOrderDto.Description,
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl
            });

            if (paymentResult == null) return null;

            await _orderService.UpdateOrderInfoAsync(
                newOrderDto.OrderCode.ToString(),
                paymentResult.QrCodeUrl,
                paymentResult.CheckoutUrl,
                paymentResult.PaymentLinkId
            );

            return new
            {
                orderCode = newOrderDto.OrderCode,
                amount = newOrderDto.Amount,
                description = newOrderDto.Description,
                qrCode = paymentResult.QrCodeUrl,
                checkoutUrl = paymentResult.CheckoutUrl,
                paymentLinkId = paymentResult.PaymentLinkId
            };
        }

        /// <summary>
        /// Hủy bỏ giao dịch thanh toán theo yêu cầu chủ động từ khách hàng.
        /// </summary>
        public async Task<bool> CancelPaymentAsync(int userId, string orderCode)
        {
            var orderDetail = await _orderRepo.FindDetailByCodeAsync(orderCode);
            if (orderDetail == null || string.IsNullOrEmpty(orderDetail.PaymentLinkId)) return false;

            var isGatewayCanceled = await _paymentGateway.CancelPaymentAsync(orderDetail.PaymentLinkId, "Khách hàng chủ động hủy đơn");
            if (!isGatewayCanceled) return false;

            var fulfillment = await _orderService.CompleteOrderAsync(orderCode, OrderStatus.canceled, userId);
            if (fulfillment != null)
            {
                await _contextHubNotify(userId, fulfillment.DocumentId, OrderStatus.canceled);
            }

            return true;
        }

        /// <summary>
        /// Tiếp nhận và xử lý dữ liệu phản hồi (Webhook) bảo mật từ cổng thanh toán.
        /// </summary>
        public async Task<bool> ProcessWebhookAsync(string rawBody, string receivedSignature)
        {
            var jsonBody = JObject.Parse(rawBody);
            var dataJson = jsonBody["data"];
            if (dataJson == null) return false;

            string orderCode = dataJson["orderCode"]?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(orderCode)) return false;

            bool isSecure = await _paymentGateway.VerifyWebhookSignatureAsync(rawBody, receivedSignature);
            if (!isSecure) return false;

            string gatewayCode = dataJson["code"]?.ToString() ?? "01";
            OrderStatus targetStatus = _paymentGateway.MapGatewayStatusToSystemStatus(gatewayCode);

            var fulfillment = await _orderService.CompleteOrderAsync(orderCode, targetStatus);
            if (fulfillment == null) return false;
            if (fulfillment.UserId == 0) return true;

            if (targetStatus == OrderStatus.canceled || targetStatus == OrderStatus.failed)
            {
                await _contextHubNotify(fulfillment.UserId, fulfillment.DocumentId, targetStatus);
                return true;
            }

            // 🎯 ĐẢM BẢO 100% NHƯ CŨ: Truyền nguyên Object DTO đã bốc thành công từ luồng chính vào Hangfire
            _backgroundJobClient.Enqueue<PaymentService>(x =>
                x.ExecuteFulfillmentWorkflowAsync(fulfillment, targetStatus));

            return true;
        }

        /// <summary>
        /// Thực thi luồng xử lý hoàn tất hóa đơn (Kích hoạt SignalR thông báo thời gian thực trước để Web reload ngay, sau đó mới gửi mail xác nhận qua MailService đã tiêm).
        /// </summary>
        [AutomaticRetry(Attempts = 5)]
        public async Task ExecuteFulfillmentWorkflowAsync(OrderFulfillmentDto fulfillment, OrderStatus targetStatus)
        {
            // 1. Phát tín hiệu SignalR lên trước để Client nhận được là reload/chuyển trang ngay lập tức
            await _contextHubNotify(fulfillment.UserId, fulfillment.DocumentId, targetStatus);

            // 2. Sử dụng trực tiếp MailService đã tiêm ở Constructor để gửi mail xác nhận
            if (!string.IsNullOrEmpty(fulfillment.UserEmail))
            {
                await _mailService.SendPaymentSuccessEmailAsync(
                    fulfillment.UserEmail,
                    fulfillment.DocumentTitle ?? "Tài liệu số");
            }
        }

        /// <summary>
        /// Phát tín hiệu thông báo trạng thái giao dịch thời gian thực qua kênh SignalR.
        /// </summary>
        private async Task _contextHubNotify(int userId, int documentId, OrderStatus status)
        {
            string groupName = $"User_{userId}";
            await _hubContext.Clients.Group(groupName).SendAsync("ReceivePaymentUpdate", new
            {
                status = status.ToString(),
                documentId = documentId
            });
        }

        /// <summary>
        /// Kích hoạt tiến trình chạy ngầm qua nền tảng Hangfire để xử lý hoàn tất đơn hàng.
        /// </summary>
        private void _executeBackgroundFulfillment(OrderFulfillmentDto fulfillment)
        {
            _backgroundJobClient.Enqueue<PaymentService>(x => x.ExecuteFulfillmentWorkflowAsync(fulfillment, OrderStatus.paid));
        }
    }
}







//using Microsoft.AspNetCore.SignalR;
//using Microsoft.Extensions.DependencyInjection;
//using Newtonsoft.Json.Linq;
//using System;
//using System.IO;
//using System.Threading.Tasks;
//using thuvienso.DTOs;
//using thuvienso.Hubs;
//using thuvienso.Interfaces;
//using thuvienso.Repositories;
//using thuvienso.Services.PaymentGateway;
//using thuvienso.Models;
//using Microsoft.Extensions.Configuration;

//namespace thuvienso.Services
//{
//    public class PaymentService
//    {
//        private readonly OrderService _orderService;
//        private readonly IPaymentGateway _paymentGateway;
//        private readonly IHubContext<PaymentHub> _hubContext;
//        private readonly IServiceScopeFactory _scopeFactory;
//        private readonly OrderRepository _orderRepo;
//        private readonly IConfiguration _config;

//        public PaymentService(
//            OrderService orderService,
//            IPaymentGateway paymentGateway,
//            IHubContext<PaymentHub> hubContext,
//            IServiceScopeFactory scopeFactory,
//            OrderRepository orderRepo,
//            IConfiguration config)
//        {
//            _orderService = orderService;
//            _paymentGateway = paymentGateway;
//            _hubContext = hubContext;
//            _scopeFactory = scopeFactory;
//            _orderRepo = orderRepo;
//            _config = config;
//        }

//        public async Task<object?> CreatePaymentLinkAsync(int userId, int documentId, int percent)
//        {
//            var baseUrl = _config["PublicUrl"];
//            var cancelUrl = $"{baseUrl}/document/{documentId}";
//            var returnUrl = $"{baseUrl}/user/profile";

//            int percentValue = Math.Clamp(percent, 1, 100);

//            // =========================================================================
//            // BƯỚC 1: TRUY QUÉT DATABASE ĐỂ TÌM ĐƠN PENDING CŨ (DỌN DẸP CUỐN CHIẾU)
//            // =========================================================================
//            var oldPendingDetail = await _orderRepo.FindLatestDetailByPairAsync(userId, documentId);

//            if (oldPendingDetail != null && oldPendingDetail.Status == OrderStatus.pending)
//            {
//                // TÁI SỬ DỤNG LINK CŨ: Nếu trùng mốc phần trăm VÀ còn hạn dưới 15 phút
//                if (oldPendingDetail.PercentPaid == percentValue && 
//                    (DateTime.UtcNow - oldPendingDetail.CreatedAt <= TimeSpan.FromMinutes(15)))
//                {
//                    return new
//                    {
//                        orderCode = long.Parse(oldPendingDetail.OrderCode),
//                        amount = (int)oldPendingDetail.PricePaid,
//                        description = $"TL{documentId}M{percentValue}",
//                        qrCode = oldPendingDetail.QrCodeUrl,
//                        checkoutUrl = oldPendingDetail.CheckoutUrl,
//                        paymentLinkId = oldPendingDetail.PaymentLinkId
//                    };
//                }

//                // =========================================================================
//                // BƯỚC 2 + 3: ÁP DỤNG PHÒNG THỦ (CHECK CỔNG TRƯỚC KHI HỦY ĐƠN TREO KHÁC MỐC/QUÁ HẠN)
//                // =========================================================================
//                if (!string.IsNullOrEmpty(oldPendingDetail.PaymentLinkId))
//                {
//                    try
//                    {
//                        string? actualGatewayStatus = await _paymentGateway.GetPaymentStatusAsync(oldPendingDetail.PaymentLinkId);

//                        // Ngoại lệ Race Condition: Khách đã trả tiền thành công bên ngoài
//                        if (actualGatewayStatus == "PAID")
//                        {
//                            var fulfillment = await _orderService.CompleteOrderAsync(oldPendingDetail.OrderCode, OrderStatus.paid);
//                            if (fulfillment != null && fulfillment.UserId != 0)
//                            {
//                                _executeBackgroundFulfillment(fulfillment);
//                            }
//                            return new { isPaidAlready = true, message = "Đơn hàng trước đó của bạn đã được thanh toán thành công!" };
//                        }

//                        // Trạng thái an toàn treo: Gọi cổng hủy triệt để link cũ
//                        if (actualGatewayStatus == "PENDING" || actualGatewayStatus == "EXPIRED")
//                        {
//                            await _paymentGateway.CancelPaymentAsync(oldPendingDetail.PaymentLinkId, "Change My Mind");
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine($"[PayOS Safety Clean-up Exception]: {ex.Message}. Bỏ qua để tạo đơn mới.");
//                    }
//                }

//                // BƯỚC 4: ĐÓNG SỔ ĐƠN CŨ THÀNH CANCELED (Ghi nhận do hệ thống dọn dẹp)
//                await _orderService.CompleteOrderAsync(oldPendingDetail.OrderCode, OrderStatus.canceled, canceledById: null);
//            }

//            // =========================================================================
//            // BƯỚC 5: TẠO ĐƠN HÀNG MỚI (DB LÚC NÀY ĐÃ SẠCH BÓNG)
//            // =========================================================================
//            var newOrderDto = await _orderService.CreateOrderAsync(userId, documentId, percentValue);
//            if (newOrderDto == null) return null;

//            // Gọi PayOS lấy mã QR mới tinh
//            var paymentResult = await _paymentGateway.CreatePaymentUrlAsync(new CreateOrderPaymentGatewayRequest
//            {
//                OrderCode = newOrderDto.OrderCode,
//                Amount = newOrderDto.Amount,
//                Description = newOrderDto.Description,
//                ReturnUrl = returnUrl,
//                CancelUrl = cancelUrl
//            });

//            if (paymentResult == null) return null;

//            // Cập nhật thông tin QR mới vào DB
//            await _orderService.UpdateOrderInfoAsync(
//                newOrderDto.OrderCode.ToString(),
//                paymentResult.QrCodeUrl,
//                paymentResult.CheckoutUrl,
//                paymentResult.PaymentLinkId
//            );

//            return new
//            {
//                orderCode = newOrderDto.OrderCode,
//                amount = newOrderDto.Amount,
//                description = newOrderDto.Description,
//                qrCode = paymentResult.QrCodeUrl,
//                checkoutUrl = paymentResult.CheckoutUrl,
//                paymentLinkId = paymentResult.PaymentLinkId
//            };
//        }

//        public async Task<bool> CancelPaymentAsync(int userId, string orderCode)
//        {
//            var orderDetail = await _orderRepo.FindDetailByCodeAsync(orderCode);
//            if (orderDetail == null || string.IsNullOrEmpty(orderDetail.PaymentLinkId)) return false;

//            var isGatewayCanceled = await _paymentGateway.CancelPaymentAsync(orderDetail.PaymentLinkId, "Khách hàng chủ động hủy đơn");
//            if (!isGatewayCanceled) return false;

//            // Truyền userId vào đây để đánh dấu ĐÚNG nguồn hủy là từ Customer chủ động
//            var fulfillment = await _orderService.CompleteOrderAsync(orderCode, OrderStatus.canceled, userId);
//            if (fulfillment != null)
//            {
//                await _contextHubNotify(userId, fulfillment.DocumentId, OrderStatus.canceled);
//            }

//            return true;
//        }

//        public async Task<bool> ProcessWebhookAsync(string rawBody, string receivedSignature)
//        {
//            var jsonBody = JObject.Parse(rawBody);
//            var dataJson = jsonBody["data"];
//            if (dataJson == null) return false;

//            string orderCode = dataJson["orderCode"]?.ToString() ?? string.Empty;
//            if (string.IsNullOrEmpty(orderCode)) return false;

//            bool isSecure = await _paymentGateway.VerifyWebhookSignatureAsync(rawBody, receivedSignature);
//            if (!isSecure) return false;

//            string gatewayCode = dataJson["code"]?.ToString() ?? "01";
//            OrderStatus targetStatus = _paymentGateway.MapGatewayStatusToSystemStatus(gatewayCode);

//            var fulfillment = await _orderService.CompleteOrderAsync(orderCode, targetStatus);
//            if (fulfillment == null) return false;
//            if (fulfillment.UserId == 0) return true; // Đơn đã xử lý trước đó

//            if (targetStatus == OrderStatus.canceled || targetStatus == OrderStatus.failed)
//            {
//                await _contextHubNotify(fulfillment.UserId, fulfillment.DocumentId, targetStatus);
//                return true;
//            }

//            _ = Task.Run(async () =>
//            {
//                try
//                {
//                    using var scope = _scopeFactory.CreateScope();
//                    var documentService = scope.ServiceProvider.GetRequiredService<DocumentService>();
//                    var mailService = scope.ServiceProvider.GetRequiredService<MailService>();

//                    await documentService.ProcessPhysicalFileAsync(
//                        fulfillment.UserId, fulfillment.DocumentId, fulfillment.FileUrl, fulfillment.PercentPaid);

//                    if (!string.IsNullOrEmpty(fulfillment.UserEmail))
//                    {
//                        await mailService.SendPaymentSuccessEmailAsync(fulfillment.UserEmail, fulfillment.DocumentTitle ?? "Tài liệu số");
//                    }

//                    await _contextHubNotify(fulfillment.UserId, fulfillment.DocumentId, targetStatus);
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"[Payment Background Critical Error]: {ex.Message}");
//                }
//            });

//            return true;
//        }

//        private async Task _contextHubNotify(int userId, int documentId, OrderStatus status)
//        {
//            string groupName = $"User_{userId}";
//            await _hubContext.Clients.Group(groupName).SendAsync("ReceivePaymentUpdate", new
//            {
//                status = status.ToString(),
//                documentId = documentId
//            });
//        }

//        private void _executeBackgroundFulfillment(OrderFulfillmentDto fulfillment)
//        {
//            _ = Task.Run(async () =>
//            {
//                try
//                {
//                    using var scope = _scopeFactory.CreateScope();
//                    var documentService = scope.ServiceProvider.GetRequiredService<DocumentService>();
//                    var mailService = scope.ServiceProvider.GetRequiredService<MailService>();

//                    await documentService.ProcessPhysicalFileAsync(
//                        fulfillment.UserId, fulfillment.DocumentId, fulfillment.FileUrl, fulfillment.PercentPaid);

//                    if (!string.IsNullOrEmpty(fulfillment.UserEmail))
//                    {
//                        await mailService.SendPaymentSuccessEmailAsync(fulfillment.UserEmail, fulfillment.DocumentTitle ?? "Tài liệu số");
//                    }

//                    await _contextHubNotify(fulfillment.UserId, fulfillment.DocumentId, OrderStatus.paid);
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"[Payment Background Fulfillment Error]: {ex.Message}");
//                }
//            });
//        }
//    }
//}
