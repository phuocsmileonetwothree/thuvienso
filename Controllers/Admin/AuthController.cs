using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using thuvienso.Data;
using thuvienso.Models;
using static QRCoder.PayloadGenerator;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý xác thực và phân quyền khu vực Admin
/// </summary>
[Route("admin/auth")]
public class AuthController : Controller
{
    private readonly AppDbContext _context;

    public AuthController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Hiển thị trang đăng nhập hệ thống Admin
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login()
    {
        return View("~/Views/Admin/Auth/Login.cshtml");
    }

    /// <summary>
    /// Xử lý logic đăng nhập, kiểm tra thông tin và cấp Cookie xác thực
    /// </summary>
    /// <param name="username">Email đăng nhập của người dùng</param>
    /// <param name="password">Mật khẩu chưa mã hóa</param>
    [HttpPost("login")]
    public async Task<IActionResult> Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ViewBag.Error = "Vui lòng nhập đầy đủ thông tin.";
            return View("~/Views/Admin/Auth/Login.cshtml");
        }

        // Tìm kiếm người dùng dựa trên tài khoản Email
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == username);

        // Xác thực mật khẩu bằng thư viện BCrypt
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
        {
            ViewBag.Error = "Sai tên đăng nhập hoặc mật khẩu.";
            return View("~/Views/Admin/Auth/Login.cshtml");
        }

        if (user != null)
        {
            // Khởi tạo danh sách quyền định danh cá nhân (Claims)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };

            var identity = new ClaimsIdentity(claims, "AdminAuth");
            var principal = new ClaimsPrincipal(identity);

            // Ghi nhận trạng thái đăng nhập vào hệ thống Cookie mã hóa thương hiệu "AdminAuth"
            await HttpContext.SignInAsync("AdminAuth", principal);
            return Redirect("/admin/dashboard");
        }

        ViewBag.Error = "Sai tên đăng nhập hoặc mật khẩu.";
        return View("~/Views/Admin/Auth/Login.cshtml");
    }

    /// <summary>
    /// Đăng xuất khỏi hệ thống, xóa Cookie xác thực hiện tại
    /// </summary>
    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AdminAuth");
        return Redirect("/admin/auth/login");
    }

    /// <summary>
    /// Hiển thị thông báo khi tài khoản truy cập trái phép hoặc không đủ quyền hạn
    /// </summary>
    [HttpGet("denied")]
    public IActionResult Denied() => Content("Bạn không có quyền.");
}
