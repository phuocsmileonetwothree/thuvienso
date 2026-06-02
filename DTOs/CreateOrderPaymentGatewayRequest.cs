namespace thuvienso.DTOs
{
    public class CreateOrderPaymentGatewayRequest
    {
        public long OrderCode { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
        public int ExpireInMinutes { get; set; } = 15;
    }
}
