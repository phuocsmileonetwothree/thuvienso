
namespace thuvienso.DTOs
{
    public class CreateOrderPaymentGatewayResponse
    {
        public string CheckoutUrl { get; set; } = string.Empty;
        public string QrCodeUrl { get; set; } = string.Empty;
        public string PaymentLinkId { get; set; } = string.Empty;
    }
}
