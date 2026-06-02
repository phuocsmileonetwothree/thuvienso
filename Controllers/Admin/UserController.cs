using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Models;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý danh sách và thông tin tài khoản người dùng (Users) khu vực Admin.
/// Tích hợp bộ lọc tìm kiếm đa thuộc tính, cơ chế phân quyền Authorize dựa trên Scheme AdminAuth,
/// và tối ưu hóa xử lý lỗi validation để giữ lại dữ liệu Form thay vì lạm dụng Redirect làm mất trải nghiệm.
/// </summary>
[Authorize(Roles = "admin", AuthenticationSchemes = "AdminAuth")]
[Route("admin/user")]
public class UserController : Controller
{
    private readonly AppDbContext _context;

    public UserController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Hiển thị danh sách người dùng, hỗ trợ tìm kiếm nâng cao theo Họ tên, Email, Số điện thoại và lọc theo vai trò
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, UserRole? role)
    {
        var query = _context.Users.AsQueryable();

        // 1. Tìm kiếm đa điều kiện (Họ tên, Email, Số điện thoại)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(u =>
                u.Name.ToLower().Contains(searchLower) ||
                u.Email.ToLower().Contains(searchLower) ||
                (u.Phone != null && u.Phone.Contains(searchLower)));
        }

        // 2. Lọc theo vai trò người dùng (Chỉ lọc nếu giá trị Enum truyền vào hợp lệ)
        if (role.HasValue && ModelState.IsValid)
        {
            query = query.Where(u => u.Role == role);
        }

        // Sắp xếp danh sách theo thứ tự vai trò trước, sau đó sắp xếp theo tên bảng chữ cái
        var users = await query
            .OrderBy(u => u.Role)
            .ThenBy(u => u.Name)
            .ToListAsync();

        // Giữ trạng thái form lọc trên UI
        ViewBag.Search = search;
        ViewBag.Role = role;

        return View("~/Views/Admin/User/Index.cshtml", users);
    }

    /// <summary>
    /// Hiển thị giao diện Form tạo mới người dùng
    /// </summary>
    [HttpGet("create")]
    public IActionResult Create()
    {
        // Truyền object tạo mới để View check Model?.Role không bị sập
        return View("~/Views/Admin/User/Create.cshtml", new Models.User { Role = UserRole.user });
    }

    /// <summary>
    /// Tiếp nhận dữ liệu, kiểm tra hợp lệ logic, mã hóa mật khẩu bằng BCrypt và thêm mới người dùng
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(string name, string email, string password, string confirmPassword, UserRole role)
    {
        // 1. Kiểm tra các trường dữ liệu bắt buộc nhập (Dùng tham số rời y chang Edit)
        if (string.IsNullOrWhiteSpace(name))
            ModelState.AddModelError("Name", "Họ tên không được để trống.");

        if (string.IsNullOrWhiteSpace(email))
            ModelState.AddModelError("Email", "Email không được để trống.");

        if (string.IsNullOrWhiteSpace(password))
            ModelState.AddModelError("Password", "Mật khẩu không được để trống.");

        // 2. Kiểm tra trùng lặp email hệ thống
        if (!string.IsNullOrWhiteSpace(email) && await _context.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError("Email", "Email này đã được sử dụng.");
        }

        // 3. Xác thực tính trùng khớp của mật khẩu nhập lại
        if (!string.IsNullOrWhiteSpace(password) && password != confirmPassword)
        {
            ModelState.AddModelError("ConfirmPassword", "Mật khẩu và xác nhận mật khẩu không khớp.");
        }
        Console.WriteLine(role + "----------------------");
        // Nếu có lỗi validation, gói dữ liệu vào Object User để View hiển thị lại form cũ
        if (!ModelState.IsValid)
        {
            var backToViewUser = new Models.User
            {
                Name = name,
                Email = email,
                Role = role
            };
            return View("~/Views/Admin/User/Create.cshtml", backToViewUser);
        }

        // Nếu mọi thứ ngon lành, tiến hành lưu vào DB
        var newUser = new Models.User
        {
            Name = name.Trim(),
            Email = email.Trim().ToLower(),
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role // Gán trực tiếp từ tham số rời cực kỳ an toàn
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Tạo người dùng thành công.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Lấy thông tin tài khoản hiện tại hiển thị lên Form chỉnh sửa
    /// </summary>
    [HttpGet("edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        return View("~/Views/Admin/User/Edit.cshtml", user);
    }

    /// <summary>
    /// Cập nhật thông tin tài khoản người dùng, hỗ trợ thay đổi mật khẩu tùy chọn nếu được nhập
    /// </summary>
    [HttpPost("edit/{id:int}")]
    public async Task<IActionResult> Edit(int id, string name, UserRole role, string? newPassword, string? confirmNewPassword)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        // 1. Kiểm tra dữ liệu bắt buộc
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError("Name", "Họ tên không được để trống.");
        }

        // 2. Nếu quản trị viên muốn thay đổi mật khẩu cho người dùng này
        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (newPassword != confirmNewPassword)
            {
                ModelState.AddModelError("ConfirmNewPassword", "Mật khẩu mới và xác nhận mật khẩu không khớp.");
            }
            else
            {
                user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            }
        }

        if (!ModelState.IsValid)
        {
            // Gán lại dữ liệu tạm thời để View không bị lỗi hiển thị dữ liệu cũ của Model
            user.Name = name;
            user.Role = role;
            return View("~/Views/Admin/User/Edit.cshtml", user);
        }

        user.Name = name.Trim();
        user.Role = role;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Cập nhật người dùng thành công.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Xóa tài khoản người dùng ra khỏi hệ thống dựa trên ID
    /// </summary>
    [HttpGet("delete/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Xóa người dùng thành công.";
        return RedirectToAction(nameof(Index));
    }
}
