using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using thuvienso.Models;
using thuvienso.Repositories;

namespace thuvienso.Services
{
    public class OrderService
    {
        private readonly OrderRepository _orderRepo;
        private readonly DocumentRepository _documentRepo;

        public OrderService(OrderRepository orderRepo, DocumentRepository documentRepo)
        {
            _orderRepo = orderRepo;
            _documentRepo = documentRepo;
        }

        /// <summary>
        /// Khởi tạo hóa đơn mới dựa trên hiệu số phần trăm mua thêm.
        /// </summary>
        public async Task<OrderGenerationDto?> CreateOrderAsync(int userId, int documentId, int percentValue)
        {
            var targetDocument = await _documentRepo.FindByIdAsync(documentId);
            if (targetDocument == null) return null;

            decimal documentPrice = targetDocument.Price ?? 0;
            if (documentPrice <= 0) documentPrice = 999999; // Giá mặc định phòng hộ

            var currentOrder = await _orderRepo.FindByPairAsync(userId, documentId);
            decimal percentOld = 0;

            if (currentOrder?.OrderDetails != null && currentOrder.OrderDetails.Any(od => od.Status == OrderStatus.paid))
            {
                percentOld = currentOrder.OrderDetails
                    .Where(od => od.Status == OrderStatus.paid)
                    .Max(od => od.PercentPaid);
            }

            // Chặn đứng nếu mốc mới nhỏ hơn hoặc bằng mốc đã sở hữu thành công
            if (percentValue <= percentOld) return null;

            // Tính tiền dựa trên hiệu số phần trăm mua thêm
            var addedPercent = percentValue - percentOld;
            decimal amount = Math.Round(documentPrice * addedPercent / 100m, 2);

            // Sinh mã OrderCode duy nhất kết hợp mã hóa ngẫu nhiên bảo mật
            var random = new Random();
            long orderCode = long.Parse(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() + random.Next(1000, 9999).ToString());
            string description = $"TL{documentId}M{percentValue}";

            if (currentOrder == null)
            {
                currentOrder = new Order
                {
                    UserId = userId,
                    DocumentId = documentId,
                    TotalPrice = documentPrice,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _orderRepo.CreateAsync(currentOrder);
                await _orderRepo.SaveAsync();
            }

            var orderDetail = new OrderDetail
            {
                OrderId = currentOrder.Id,
                OrderCode = orderCode.ToString(),
                PercentPaid = percentValue,
                PricePaid = amount, // Lưu chuẩn decimal dưới Database
                Status = OrderStatus.pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _orderRepo.CreateDetailAsync(orderDetail);
            await _orderRepo.SaveAsync();

            return new OrderGenerationDto
            {
                OrderCode = orderCode,
                Amount = (int)Math.Round(amount, 0), // Ép kiểu int khi đẩy ra cổng thanh toán
                Description = description
            };
        }

        /// <summary>
        /// Cập nhật thông tin cổng thanh toán vào hóa đơn.
        /// </summary>
        public async Task UpdateOrderInfoAsync(string orderCode, string qr, string checkout, string linkId)
        {
            var orderDetail = await _orderRepo.FindDetailByCodeAsync(orderCode);
            if (orderDetail != null)
            {
                orderDetail.QrCodeUrl = qr;
                orderDetail.CheckoutUrl = checkout;
                orderDetail.PaymentLinkId = linkId;
                orderDetail.UpdatedAt = DateTime.UtcNow;
                await _orderRepo.SaveAsync();
            }
        }

        /// <summary>
        /// Hoàn tất trạng thái hóa đơn và phân loại nguồn hủy nếu có.
        /// </summary>
        public async Task<OrderFulfillmentDto?> CompleteOrderAsync(string orderCode, OrderStatus systemStatus, int? canceledById = null)
        {
            var orderDetail = await _orderRepo.FindDetailByCodeAsync(orderCode);
            if (orderDetail == null) return null;

            if (orderDetail.Status == systemStatus)
            {
                return new OrderFulfillmentDto { IsSuccess = false, UserId = 0 };
            }

            var orderWithData = await _orderRepo.FindByIdForFulfillmentAsync(orderDetail.OrderId);
            if (orderWithData == null) return null;

            orderDetail.Status = systemStatus;
            orderDetail.UpdatedAt = DateTime.UtcNow;

            if (systemStatus == OrderStatus.paid)
            {
                orderDetail.TransactedAt = DateTime.UtcNow;
            }
            else if (systemStatus == OrderStatus.canceled)
            {
                orderDetail.CanceledAt = DateTime.UtcNow;

                // PHÂN BIỆT RÕ RÀNG NGUỒN HỦY THEO KB3 ĐÃ PHÁT TRIỂN
                if (canceledById.HasValue)
                {
                    orderDetail.CanceledBy = CancelReasonSource.customer;
                    orderDetail.CanceledById = canceledById.Value;
                }
                else
                {
                    orderDetail.CanceledBy = CancelReasonSource.auto; // Hệ thống quét dọn tự động hoặc quá hạn
                    orderDetail.CanceledById = orderWithData.UserId; // Mặc định map về chủ đơn để đồng bộ cấu trúc DB
                }
            }

            await _orderRepo.SaveAsync();

            return new OrderFulfillmentDto
            {
                OrderCode = orderDetail.OrderCode,
                IsSuccess = systemStatus == OrderStatus.paid,
                UserId = orderWithData.UserId,
                UserEmail = orderWithData.User?.Email,
                DocumentId = orderWithData.DocumentId,
                DocumentTitle = orderWithData.Document?.Title,
                FileUrl = orderWithData.Document?.FileUrl,
                PercentPaid = orderDetail.PercentPaid
            };
        }
    }

    /// <summary>
    /// Đối tượng vận chuyển dữ liệu khi khởi tạo hóa đơn thành công.
    /// </summary>
    public class OrderGenerationDto
    {
        public long OrderCode { get; set; }
        public int Amount { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Đối tượng vận chuyển dữ liệu khi hoàn tất xử lý hóa đơn.
    /// </summary>
    public class OrderFulfillmentDto
    {
        public string OrderCode { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public int UserId { get; set; }
        public string? UserEmail { get; set; }
        public int DocumentId { get; set; }
        public string? DocumentTitle { get; set; }
        public string? FileUrl { get; set; }
        public decimal PercentPaid { get; set; }
    }
}
