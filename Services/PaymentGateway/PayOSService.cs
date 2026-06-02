using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using thuvienso.Interfaces;
using thuvienso.DTOs;

namespace thuvienso.Services.PaymentGateway
{
    public class PayOSService : IPaymentGateway
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public string GatewayName => "PayOS";

        public PayOSService(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<CreateOrderPaymentGatewayResponse?> CreatePaymentUrlAsync(CreateOrderPaymentGatewayRequest request)
        {
            var description = request.Description;
            if (description.Length > 9)
            {
                description = description.Substring(0, 9);
            }

            var expiredAt = (int)DateTimeOffset.UtcNow.AddMinutes(request.ExpireInMinutes).ToUnixTimeSeconds();

            // Sắp xếp các tham số theo đúng bảng chữ cái Alphabet theo tài liệu PayOS
            var rawSignature = $"amount={request.Amount}&cancelUrl={request.CancelUrl}&description={description}&orderCode={request.OrderCode}&returnUrl={request.ReturnUrl}";
            var signature = ComputeHmacSha256(rawSignature, _config["PayOS:ChecksumKey"]);

            var payload = new
            {
                orderCode = request.OrderCode,
                amount = request.Amount,
                description = description,
                cancelUrl = request.CancelUrl,
                returnUrl = request.ReturnUrl,
                expiredAt = expiredAt,
                signature = signature
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("x-client-id", _config["PayOS:ClientId"]);
            client.DefaultRequestHeaders.Add("x-api-key", _config["PayOS:ApiKey"]);

            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync("https://api-merchant.payos.vn/v2/payment-requests", content);
                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[PayOS Create Error] HTTP {response.StatusCode}: {errBody}");
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseBody);

                if (json["code"]?.ToString() != "00")
                {
                    Console.WriteLine($"[PayOS Logic Error]: {json["desc"]}");
                    return null;
                }

                var data = json["data"];
                if (data == null) return null;

                return new CreateOrderPaymentGatewayResponse
                {
                    CheckoutUrl = data["checkoutUrl"]?.ToString() ?? string.Empty,
                    QrCodeUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data={Uri.EscapeDataString(data["qrCode"]?.ToString() ?? "")}",
                    PaymentLinkId = data["paymentLinkId"]?.ToString() ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PayOS Create Critical Exception]: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> VerifyWebhookSignatureAsync(string rawBody, string receivedSignature)
        {
            try
            {
                var payload = JObject.Parse(rawBody);
                var eventData = payload["data"] as JObject;
                if (eventData == null) return false;

                // Sắp xếp các thuộc tính bên trong object data theo Alphabet để check chữ ký
                var sortedKeys = eventData.Properties().Select(p => p.Name).OrderBy(name => name).ToList();
                var signatureBase = new StringBuilder();

                foreach (var key in sortedKeys)
                {
                    signatureBase.Append($"{key}={eventData[key]?.ToString() ?? ""}");
                    if (key != sortedKeys.Last())
                        signatureBase.Append("&");
                }

                var expectedSignature = ComputeHmacSha256(signatureBase.ToString(), _config["PayOS:ChecksumKey"]);
                return receivedSignature == expectedSignature;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Webhook Verify Exception]: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CancelPaymentAsync(string paymentLinkId, string reason)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("x-client-id", _config["PayOS:ClientId"]);
            client.DefaultRequestHeaders.Add("x-api-key", _config["PayOS:ApiKey"]);

            var payload = new { cancellationReason = reason };
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync($"https://api-merchant.payos.vn/v2/payment-requests/{paymentLinkId}/cancel", content);
                if (response.IsSuccessStatusCode) return true;

                string responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[PayOS Cancel FAIL] HTTP Status: {response.StatusCode} | Phản hồi: {responseBody}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PayOS Cancel Exception]: {ex.Message}");
                return false;
            }
        }

        public async Task<string?> GetPaymentStatusAsync(string paymentLinkId)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("x-client-id", _config["PayOS:ClientId"]);
            client.DefaultRequestHeaders.Add("x-api-key", _config["PayOS:ApiKey"]);

            try
            {
                var response = await client.GetAsync($"https://api-merchant.payos.vn/v2/payment-requests/{paymentLinkId}");
                if (!response.IsSuccessStatusCode) return null;

                var responseBody = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseBody);

                // Trả về trạng thái của PayOS: PAID, PENDING, EXPIRED, CANCELLED
                return json["data"]?["status"]?.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PayOS Get Status Exception]: {ex.Message}");
                return null; // Trả về null nếu nghẽn mạng để luồng ngoài xử lý phòng thủ
            }
        }

        public thuvienso.Models.OrderStatus MapGatewayStatusToSystemStatus(string rawCode)
        {
            // Định nghĩa chính xác theo mã lỗi phản hồi của PayOS
            return rawCode switch
            {
                "00" => thuvienso.Models.OrderStatus.paid,
                "01" => thuvienso.Models.OrderStatus.failed,
                "02" => thuvienso.Models.OrderStatus.canceled,
                "04" => thuvienso.Models.OrderStatus.canceled,
                "A001" => thuvienso.Models.OrderStatus.canceled, // Người dùng chủ động hủy đơn trên UI
                _ => thuvienso.Models.OrderStatus.pending
            };
        }

        public static string ComputeHmacSha256(string rawData, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var rawBytes = Encoding.UTF8.GetBytes(rawData);
            using var hmac = new HMACSHA256(keyBytes);
            return BitConverter.ToString(hmac.ComputeHash(rawBytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
