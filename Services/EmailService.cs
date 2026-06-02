using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace thuvienso.Services
{
    public class MailService
    {
        private readonly IConfiguration _config;

        public MailService(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Gửi thư điện tử nội bộ thông qua cấu hình SMTP.
        /// </summary>
        private async Task SendEmailAsync(string to, string subject, string body)
        {
            var smtpSection = _config.GetSection("Smtp");

            using var client = new SmtpClient(smtpSection["Host"], int.Parse(smtpSection["Port"]))
            {
                Credentials = new NetworkCredential(smtpSection["User"], smtpSection["Pass"]),
                EnableSsl = true
            };

            using var message = new MailMessage
            {
                From = new MailAddress(smtpSection["User"], "Thư viện số"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(to);

            await client.SendMailAsync(message);
        }

        /// <summary>
        /// Gửi thư thông báo thanh toán đơn hàng thành công.
        /// </summary>
        public async Task SendPaymentSuccessEmailAsync(string userEmail, string documentTitle)
        {
            string publicUrl = _config["PublicUrl"] ?? "http://localhost:5000";
            string subject = $"Xác nhận thanh toán thành công – {documentTitle}";
            string body = $@"
            <html>
            <body style='font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 30px; margin: 0;'>
              <div style='max-width:600px;margin:0 auto;background:#fff;border-radius:6px;border:1px solid #dee2e6;overflow:hidden;'>
                <div style='background:#0d6efd;color:white;padding:20px 30px;'>
                  <h2 style='margin:0;font-size:20px;text-align: center'>Bạn đã thanh toán thành công</h2>
                </div>
                <div style='padding:30px;'>
                  <h3 style='color:#0d6efd; margin-top:0;'>Tài liệu: {documentTitle}</h3>
                  <p>Cảm ơn bạn đã ủng hộ <strong>Thư Viện Số</strong>.</p>
                  <p style='text-align: center; margin-top: 30px;'>
                    <a href='{publicUrl}/user/profile' style='display: inline-block; background: #0d6efd; color: white; padding: 12px 25px; border-radius: 4px; text-decoration: none; font-weight: bold;'>Truy cập tài liệu của bạn</a>
                  </p>
                </div>
              </div>
            </body>
            </html>";

            await SendEmailAsync(userEmail, subject, body);
        }

        /// <summary>
        /// Gửi thư chứa mã xác nhận đặt lại mật khẩu tài khoản.
        /// </summary>
        public async Task SendForgotPasswordEmailAsync(string userEmail, string userName, string code)
        {
            string subject = "Mã đặt lại mật khẩu – Thư Viện Số";
            string body = $@"
            <!DOCTYPE html>
            <html>
            <body style='font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 30px; margin: 0;'>
              <div style='max-width:600px;margin:0 auto;background:#fff;border-radius:6px;border:1px solid #dee2e6;overflow:hidden;'>
                <div style='background:#0d6efd;color:white;padding:20px 30px;'>
                  <h2 style='margin:0;font-size:20px;text-align:center;'>Yêu cầu đặt lại mật khẩu</h2>
                </div>
                <div style='padding:30px;'>
                  <p>Xin chào <strong>{userName}</strong>,</p>
                  <p>Bạn (hoặc ai đó) vừa yêu cầu đặt lại mật khẩu cho tài khoản Thư Viện Số.</p>
                  <p>Mã xác nhận của bạn là:</p>
                  <div style='font-size:24px; font-weight:bold; color:#0d6efd; text-align:center; padding:10px 0;'>{code}</div>
                  <p>Mã có hiệu lực trong <strong>30 phút</strong>. Vui lòng không chia sẻ mã này cho bất kỳ ai.</p>
                </div>
                <div style='background:#f1f3f5;color:#6c757d;text-align:center;padding:15px;'>
                  <small>© {DateTime.UtcNow.Year} <strong>Thư Viện Số</strong> – Email tự động, vui lòng không trả lời</small>
                </div>
              </div>
            </body>
            </html>";

            await SendEmailAsync(userEmail, subject, body);
        }

        /// <summary>
        /// Gửi thư phản hồi thông tin liên hệ của khách hàng.
        /// </summary>
        public async Task SendContactResponseEmailAsync(string toEmail, string name, string phone, string subject, string message)
        {
            string mailSubject = $"Phản hồi của Thư Viện Số: {subject}";
            string body = $@"
            <!DOCTYPE html>
            <html>
            <body style='font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 30px; margin: 0;'>
              <div style='max-width:600px;margin:0 auto;background:#fff;border-radius:6px;border:1px solid #dee2e6;overflow:hidden;'>
                <div style='background:#0d6efd;color:white;padding:20px 30px;'>
                  <h2 style='margin:0;font-size:20px;text-align: center'>Chúng tôi sẽ liên hệ lại sớm nhất có thể</h2>
                </div>
                <div style='padding:30px;'>
                  <h3 style='color:#0d6efd; margin-top:0;'>Liên hệ mới từ {name}</h3>
                  <p><strong>Email:</strong> {toEmail}</p>
                  <p><strong>SĐT:</strong> {phone}</p>
                  <p><strong>Tiêu đề:</strong> {subject}</p>
                  <p><strong>Nội dung:</strong></p>
                  <div style='background:#f1f3f5;padding:12px 16px;border-left:4px solid #0d6efd;border-radius:4px;white-space:pre-line;'>{message}</div>
                </div>
                <div style='background:#f1f3f5;color:#6c757d;text-align:center;padding:15px;'>
                  <small>© {DateTime.UtcNow.Year} <strong>Thư Viện Số</strong> – Email tự động, vui lòng không trả lời</small>
                </div>
              </div>
            </body>
            </html>";

            await SendEmailAsync(toEmail, mailSubject, body);
        }
    }
}
