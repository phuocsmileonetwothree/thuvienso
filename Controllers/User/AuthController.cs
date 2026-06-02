using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using thuvienso.Data;
using thuvienso.Helpers;
using thuvienso.Models;
using thuvienso.Services;
using Hangfire;

namespace thuvienso.Controllers.User;

[Route("user/auth")]
public class AuthController : Controller
{
    private readonly AppDbContext _context;
    private readonly MailService _mailService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AuthController(AppDbContext context, MailService mailService, IBackgroundJobClient backgroundJobClient)
    {
        _context = context;
        _mailService = mailService;
        _backgroundJobClient = backgroundJobClient;
    }

    /// <summary>
    /// Hiển thị giao diện trang đăng nhập hệ thống.
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login(string returnUrl = "/")
    {
        ViewBag.ReturnUrl = returnUrl;
        return View("~/Views/User/Auth/Login.cshtml");
    }

    /// <summary>
    /// Thực hiện xác thực tài khoản và thiết lập phiên đăng nhập.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> LoginPost(string email, string password)
    {
        var returnUrl = Request.Query["returnUrl"].FirstOrDefault() ?? "/";

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
        {
            TempData["Error"] = "Email hoặc mật khẩu không đúng.";
            return Redirect($"/user/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        HttpContext.Session.SetInt32("UserId", user.Id);
        HttpContext.Session.SetString("Name", user.Name);

        return Redirect(returnUrl);
    }

    /// <summary>
    /// Hủy bỏ phiên đăng nhập và xóa dữ liệu Session.
    /// </summary>
    [HttpGet("logout")]
    public IActionResult Logout()
    {
        HttpContext.Session.Remove("UserId");
        return Redirect("/user/auth/login");
    }

    /// <summary>
    /// Hiển thị giao diện trang đăng ký tài khoản mới.
    /// </summary>
    [HttpGet("register")]
    public IActionResult Register()
    {
        return View("~/Views/User/Auth/Register.cshtml");
    }

    /// <summary>
    /// Kiểm tra thông tin và khởi tạo tài khoản người dùng mới.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterPost(string name, string email, string phone, string password, string rePassword)
    {
        if(password != rePassword)
        {
            TempData["Error"] = "Mật khẩu không trùng khớp";
            return Redirect("/user/auth/register");
        }
        if (await _context.Users.AnyAsync(u => u.Email == email))
        {
            TempData["Error"] = "Email đã được sử dụng.";
            return Redirect("/user/auth/register");
        }

        if (await _context.Users.AnyAsync(u => u.Phone == phone))
        {
            TempData["Error"] = "SĐT đã được sử dụng.";
            return Redirect("/user/auth/register");
        }

        var user = new Models.User
        {
            Name = name,
            Email = email,
            Phone = phone,
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.user
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Đăng ký thành công. Vui lòng đăng nhập.";
        return Redirect("/user/auth/login");
    }

    /// <summary>
    /// Hiển thị giao diện yêu cầu đặt lại mật khẩu.
    /// </summary>
    [HttpGet("forgot")]
    public IActionResult ForgotPassword()
    {
        return View("~/Views/User/Auth/ForgotPassword.cshtml");
    }

    /// <summary>
    /// Khởi tạo mã khôi phục mật khẩu và đẩy tiến trình gửi thư vào Hangfire.
    /// </summary>
    [HttpPost("forgot")]
    public async Task<IActionResult> ForgotPasswordPost(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            TempData["Error"] = "Email không tồn tại.";
            return Redirect("/user/auth/forgot");
        }

        var random = new Random();
        var code = random.Next(100000, 999999).ToString();

        user.ResetCode = code;
        user.ResetCodeExpiry = DateTime.UtcNow.AddMinutes(30);
        await _context.SaveChangesAsync();

        _backgroundJobClient.Enqueue<MailService>(x =>
            x.SendForgotPasswordEmailAsync(user.Email, user.Name, code));

        TempData["Success"] = "Đã gửi mã xác nhận đến email của bạn.";
        return Redirect($"/user/auth/verify?email={email}");
    }

    /// <summary>
    /// Hiển thị giao diện xác thực mã khôi phục mật khẩu.
    /// </summary>
    [HttpGet("verify")]
    public IActionResult VerifyResetCode(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Thiếu thông tin email.";
            return Redirect("/user/auth/forgot");
        }

        return View("~/Views/User/Auth/Verify.cshtml", model: email);
    }

    /// <summary>
    /// Kiểm tra mã khôi phục và tiến hành cập nhật mật khẩu mới.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyResetCodePost(string email, string code, string newPassword, string reNewPassword)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || user.ResetCode != code || user.ResetCodeExpiry < DateTime.UtcNow)
        {
            TempData["Error"] = "Mã không hợp lệ hoặc đã hết hạn.";
            return Redirect($"/user/auth/verify?email={email}");
        }

        if (newPassword != reNewPassword)
        {
            TempData["Error"] = "Mật khẩu mới không trùng khớp.";
            return Redirect($"/user/auth/verify?email={email}");
        }

        user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.ResetCode = null;
        user.ResetCodeExpiry = null;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Mật khẩu đã được thay đổi thành công. Vui lòng đăng nhập.";
        return Redirect("/user/auth/login");
    }
}
