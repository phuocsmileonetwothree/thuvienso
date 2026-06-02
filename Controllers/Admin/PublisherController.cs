using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Models;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý danh mục Nhà xuất bản (Publishers) khu vực Admin.
/// Hiện tại đang tương tác trực tiếp với AppDbContext, sẵn sàng để bốc tách sang PublisherRepository đồng bộ với hệ thống.
/// </summary>
[Route("admin/publisher")]
public class PublisherController : Controller
{
    private readonly AppDbContext _context;

    public PublisherController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Hiển thị danh sách nhà xuất bản kết hợp bộ lọc tìm kiếm theo tên và phân trang dữ liệu
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        int pageSize = 10;
        var query = _context.Publishers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search));
        }

        var totalItems = await query.CountAsync();
        var publishers = await query
            .OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Đẩy dữ liệu cấu hình phân trang và trạng thái bộ lọc ra View
        ViewBag.Search = search;
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalItems = totalItems;

        return View("~/Views/Admin/Publisher/Index.cshtml", publishers);
    }

    /// <summary>
    /// Hiển thị giao diện Form tạo mới Nhà xuất bản
    /// </summary>
    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("~/Views/Admin/Publisher/Create.cshtml");
    }

    /// <summary>
    /// Tiếp nhận dữ liệu, kiểm tra tính hợp lệ về định dạng và tính duy nhất của tên Nhà xuất bản trước khi thêm mới
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(string name, string? description)
    {
        // 1. Kiểm tra tính hợp lệ của chuỗi nhập vào (Bao gồm ký tự chữ hoặc số)
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, ".*[A-Za-zÀ-ỹ0-9].*"))
        {
            ModelState.AddModelError("Name", "Tên nhà xuất bản không hợp lệ.");
        }
        // 2. Kiểm tra trùng lặp dữ liệu trong hệ thống
        else if (await _context.Publishers.AnyAsync(p => p.Name == name))
        {
            ModelState.AddModelError("Name", "Tên nhà xuất bản đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["Name"] = name;
            ViewData["Description"] = description;
            return View("~/Views/Admin/Publisher/Create.cshtml");
        }

        _context.Publishers.Add(new Publisher { Name = name, Description = description });
        await _context.SaveChangesAsync();

        TempData["Success"] = "Tạo nhà xuất bản thành công.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Hiển thị thông tin chi tiết của Nhà xuất bản lên Form chỉnh sửa theo mã định danh ID
    /// </summary>
    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(int id)
    {
        var publisher = await _context.Publishers.FindAsync(id);
        if (publisher == null)
            return NotFound();

        return View("~/Views/Admin/Publisher/Edit.cshtml", publisher);
    }

    /// <summary>
    /// Cập nhật thay đổi thông tin Nhà xuất bản, xử lý kiểm tra validate trùng tên loại trừ chính bản ghi hiện tại
    /// </summary>
    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(int id, string name, string? description)
    {
        var publisher = await _context.Publishers.FindAsync(id);
        if (publisher == null)
        {
            TempData["Error"] = "Không tìm thấy nhà xuất bản.";
            return RedirectToAction("Index");
        }

        // 1. Kiểm tra định dạng tên chỉnh sửa
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, ".*[A-Za-zÀ-ỹ0-9].*"))
        {
            ModelState.AddModelError("Name", "Tên nhà xuất bản không hợp lệ.");
        }
        // 2. Kiểm tra trùng tên với các bản ghi khác ngoại trừ chính nó (p.Id != id)
        else if (await _context.Publishers.AnyAsync(p => p.Name == name && p.Id != id))
        {
            ModelState.AddModelError("Name", "Tên nhà xuất bản đã tồn tại.");
        }

        if (!ModelState.IsValid)
        {
            publisher.Name = name;
            publisher.Description = description;
            return View("~/Views/Admin/Publisher/Edit.cshtml", publisher);
        }

        publisher.Name = name;
        publisher.Description = description;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Cập nhật nhà xuất bản thành công.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Xóa hoàn toàn bản ghi Nhà xuất bản ra khỏi hệ thống
    /// </summary>
    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var publisher = await _context.Publishers.FindAsync(id);
        if (publisher == null)
        {
            TempData["Error"] = "Không tìm thấy nhà xuất bản để xoá.";
            return RedirectToAction("Index");
        }

        _context.Publishers.Remove(publisher);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Xoá nhà xuất bản thành công.";
        return RedirectToAction("Index");
    }
}
