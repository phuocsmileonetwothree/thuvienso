using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using thuvienso.DTOs;
using thuvienso.Models;

namespace thuvienso.Interfaces
{
    public interface IPaymentGateway
    {
        string GatewayName { get; }

        // Đã đồng bộ documentId thành kiểu int để khớp với Service thực thi
        Task<CreateOrderPaymentGatewayResponse?> CreatePaymentUrlAsync(CreateOrderPaymentGatewayRequest request);

        // Hàm tự xác minh chữ ký bảo mật riêng của từng cổng
        Task<bool> VerifyWebhookSignatureAsync(string rawBody, string receivedSignature);

        // Hàm dịch trạng thái của riêng cổng đó về trạng thái hệ thống chuẩn (System Status Enum)
        OrderStatus MapGatewayStatusToSystemStatus(string rawCode);

        Task<bool> CancelPaymentAsync(string orderCode, string reason);

        Task<string?> GetPaymentStatusAsync(string paymentLinkId);
    }
}
