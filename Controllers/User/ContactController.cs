using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Helpers;
using thuvienso.Models;
using thuvienso.Services;
using Hangfire;

namespace thuvienso.Controllers.User
{
    [Route("contact")]
    public class ContactController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly MailService _mailService;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public ContactController(AppDbContext context, IConfiguration config, MailService mailService, IBackgroundJobClient backgroundJobClient)
        {
            _context = context;
            _config = config;
            _mailService = mailService;
            _backgroundJobClient = backgroundJobClient;
        }

        /// <summary>
        /// Hiển thị giao diện trang gửi thông tin liên hệ.
        /// </summary>
        [HttpGet("")]
        public IActionResult Index()
        {
            return View("~/Views/User/Contact/Index.cshtml");
        }

        /// <summary>
        /// Tiếp nhận thông tin góp ý, lưu vào cơ sở dữ liệu và kích hoạt tiến trình gửi thư phản hồi ngầm.
        /// </summary>
        [HttpPost("")]
        public async Task<IActionResult> SubmitContact(string name, string email, string phone, string subject, string message)
        {
            var contact = new Contact
            {
                Name = name,
                Email = email,
                Subject = subject,
                Phone = phone,
                Message = message,
                CreatedAt = DateTime.UtcNow 
            };

            _context.Contacts.Add(contact);
            await _context.SaveChangesAsync();

            _backgroundJobClient.Enqueue<MailService>(x =>
                x.SendContactResponseEmailAsync(contact.Email, name, phone, subject, message));

            TempData["Success"] = "Cảm ơn bạn đã liên hệ. Chúng tôi sẽ phản hồi sớm nhất!";
            return Redirect("/contact");
        }
    }
}
