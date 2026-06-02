using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Models;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý danh mục tác giả tại khu vực Admin (CRUD + Phân trang)
/// </summary>
[Route("admin/author")]
public class AuthorController : Controller
{
    private readonly AppDbContext _context;

    public AuthorController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Hiển thị danh sách tác giả có hỗ trợ tìm kiếm và phân trang
    /// </summary>
    /// <param name="search">Từ khóa tìm kiếm theo tên tác giả</param>
    /// <param name="page">Số trang hiện tại (mặc định là 1)</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        int pageSize = 10;
        var query = _context.Authors.AsQueryable();

        // Lọc dữ liệu theo từ khóa tìm kiếm nếu có
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a => a.Name.Contains(search));
        }

        var totalItems = await query.CountAsync();

        // Thực hiện phân trang và sắp xếp giảm dần theo ID
        var authors = await query
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Lưu trữ thông tin phân trang phục vụ hiển thị ở View
        ViewBag.Search = search;
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalItems = totalItems;

        return View("~/Views/Admin/Author/Index.cshtml", authors);
    }

    /// <summary>
    /// Hiển thị giao diện thêm mới tác giả
    /// </summary>
    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("~/Views/Admin/Author/Create.cshtml");
    }

    /// <summary>
    /// Xử lý logic lưu dữ liệu tác giả mới vào DB (Có validate trùng tên và ký tự đặc biệt)
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(string name, string? description)
    {
        // Kiểm tra tên trống hoặc chỉ chứa ký tự đặc biệt không hợp lệ
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, ".*[A-Za-zÀ-ỹ0-9].*"))
        {
            ModelState.AddModelError("Name", "Tên tác giả không hợp lệ.");
        }
        // Kiểm tra trùng lặp tên tác giả trong database
        else if (_context.Authors.Any(a => a.Name == name))
        {
            ModelState.AddModelError("Name", "Tên tác giả đã tồn tại.");
        }

        // Nếu dữ liệu không hợp lệ, trả về giao diện kèm dữ liệu cũ để người dùng sửa
        if (!ModelState.IsValid)
        {
            ViewData["Name"] = name;
            ViewData["Description"] = description;
            return View("~/Views/Admin/Author/Create.cshtml");
        }

        _context.Authors.Add(new Author { Name = name, Description = description });
        await _context.SaveChangesAsync();

        TempData["Success"] = "Tạo tác giả thành công.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Hiển thị thông tin tác giả cụ thể để tiến hành chỉnh sửa
    /// </summary>
    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(int id)
    {
        var author = await _context.Authors.FindAsync(id);
        if (author == null)
            return NotFound();

        return View("~/Views/Admin/Author/Edit.cshtml", author);
    }

    /// <summary>
    /// Cập nhật thông tin thay đổi của tác giả vào DB
    /// </summary>
    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(int id, string name, string? description)
    {
        var author = await _context.Authors.FindAsync(id);
        if (author == null)
        {
            TempData["Error"] = "Không tìm thấy tác giả.";
            return RedirectToAction("Index");
        }

        // Validate dữ liệu đầu vào của tên sửa đổi
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, ".*[A-Za-zÀ-ỹ0-9].*"))
        {
            ModelState.AddModelError("Name", "Tên tác giả không hợp lệ.");
        }
        // Kiểm tra trùng tên với tác giả khác (loại trừ chính nó thông qua Id)
        else if (_context.Authors.Any(a => a.Name == name && a.Id != id))
        {
            ModelState.AddModelError("Name", "Tên tác giả đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            author.Name = name;
            author.Description = description;
            return View("~/Views/Admin/Author/Edit.cshtml", author);
        }

        author.Name = name;
        author.Description = description;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Cập nhật tác giả thành công.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Xóa hoàn toàn bản ghi tác giả dựa trên ID
    /// </summary>
    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var author = await _context.Authors.FindAsync(id);
        if (author == null)
        {
            TempData["Error"] = "Không tìm thấy tác giả để xoá.";
            return RedirectToAction("Index");
        }

        _context.Authors.Remove(author);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Xoá tác giả thành công.";
        return RedirectToAction("Index");
    }
}
