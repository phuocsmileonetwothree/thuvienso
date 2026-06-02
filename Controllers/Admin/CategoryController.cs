using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using thuvienso.Data;
using thuvienso.Models;
using thuvienso.Helpers;
using static thuvienso.Models.Category;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý cấu trúc cây danh mục phân cấp đa tầng (Parent-Child) tại khu vực Admin
/// </summary>
[Route("admin/category")]
public class CategoryController : Controller
{
    private readonly AppDbContext _context;

    public CategoryController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Hiển thị toàn bộ cấu trúc cây danh mục và đánh dấu các nút khớp với từ khóa tìm kiếm
    /// </summary>
    /// <param name="search">Từ khóa tìm kiếm tên danh mục</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search)
    {
        // Tải toàn bộ danh mục từ database để dựng cấu trúc cây đầy đủ
        var allCategories = await _context.Categories
            .Include(c => c.Parent)
            .ToListAsync();

        // Chuyển đổi danh sách phẳng thành cấu trúc cây thông qua Helper
        var tree = CategoryHelper.BuildTree(allCategories);

        // Duyệt cây để đánh dấu thuộc tính Matched khi có từ khóa tìm kiếm
        if (!string.IsNullOrWhiteSpace(search))
        {
            foreach (var item in tree)
            {
                if (item.Category.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    item.Matched = true;
                }
            }
        }

        ViewBag.Search = search;
        return View("~/Views/Admin/Category/Index.cshtml", tree);
    }

    /// <summary>
    /// Hiển thị giao diện tạo mới danh mục
    /// </summary>
    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        var categories = await _context.Categories.ToListAsync();
        ViewBag.ParentCategories = new SelectList(categories, "Id", "Name");
        return View("~/Views/Admin/Category/Create.cshtml");
    }

    /// <summary>
    /// Xử lý logic thêm mới danh mục (Có kiểm tra tính hợp lệ của tên và chống trùng lặp)
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(string name, int? parentId, string? description)
    {
        name = name?.Trim();

        // Kiểm tra dữ liệu: Không bỏ trống và không được phép chỉ chứa ký số
        if (string.IsNullOrWhiteSpace(name) || name.All(char.IsDigit))
        {
            TempData["Error"] = "Tên danh mục không hợp lệ.";
            ViewBag.ParentCategories = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", parentId);
            return View("~/Views/Admin/Category/Create.cshtml");
        }

        // Kiểm tra trùng lặp tên danh mục (không phân biệt hoa thường)
        var exists = await _context.Categories.AnyAsync(c => c.Name.ToLower() == name.ToLower());
        if (exists)
        {
            TempData["Error"] = "Tên danh mục đã tồn tại.";
            ViewBag.ParentCategories = new SelectList(await _context.Categories.ToListAsync(), "Id", "Name", parentId);
            return View("~/Views/Admin/Category/Create.cshtml");
        }

        var category = new Category
        {
            Name = name,
            ParentId = parentId,
            Description = description
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Thêm danh mục thành công.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Hiển thị giao diện chỉnh sửa danh mục, loại bỏ chính nó và các con cháu khỏi dropdown cha
    /// </summary>
    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
            return NotFound();

        var allCategories = await _context.Categories.ToListAsync();

        // Hàm đệ quy nội bộ (Local function) tìm tất cả ID của con cháu để chặn vòng lặp vô hạn
        List<int> GetDescendantIds(int parentId)
        {
            var result = new List<int> { parentId };
            var children = allCategories.Where(c => c.ParentId == parentId).Select(c => c.Id).ToList();

            foreach (var childId in children)
            {
                result.AddRange(GetDescendantIds(childId));
            }
            return result;
        }

        var excludeIds = GetDescendantIds(id);

        // Lọc danh sách danh mục làm cha: Loại trừ chính nó và các nhánh con của nó
        var parentOptions = allCategories.Where(c => !excludeIds.Contains(c.Id)).ToList();

        ViewBag.ParentCategories = new SelectList(parentOptions, "Id", "Name", category.ParentId);
        return View("~/Views/Admin/Category/Edit.cshtml", category);
    }

    /// <summary>
    /// Cập nhật thông tin thay đổi của danh mục và xử lý dữ liệu khi có lỗi điều hướng (Invalid)
    /// </summary>
    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(int id, string name, int? parentId, string? description)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            TempData["Error"] = "Không tìm thấy danh mục.";
            return RedirectToAction("Index");
        }

        name = name?.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.All(char.IsDigit))
        {
            TempData["Error"] = "Tên không hợp lệ.";
            goto Invalid;
        }

        bool isDuplicate = await _context.Categories.AnyAsync(c => c.Id != id && c.Name == name);
        if (isDuplicate)
        {
            TempData["Error"] = "Tên danh mục đã tồn tại.";
            goto Invalid;
        }

        category.Name = name;
        category.ParentId = parentId;
        category.Description = description;
        await _context.SaveChangesAsync();

        TempData["Success"] = "Cập nhật thành công.";
        return RedirectToAction("Index");

    Invalid:
        // Điểm nhảy xử lý khi dữ liệu không hợp lệ: Nạp lại dropdown và giữ nguyên dữ liệu vừa nhập
        ViewBag.ParentCategories = new SelectList(
            await _context.Categories.Where(c => c.Id != id).ToListAsync(),
            "Id", "Name", parentId
        );
        category.Name = name;
        category.ParentId = parentId;
        category.Description = description;
        return View("~/Views/Admin/Category/Edit.cshtml", category);
    }

    /// <summary>
    /// Xóa danh mục được chỉ định dựa trên ID
    /// </summary>
    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            TempData["Error"] = "Không tìm thấy danh mục để xoá.";
            return RedirectToAction("Index");
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        TempData["Success"] = "Xoá danh mục thành công.";
        return RedirectToAction("Index");
    }
}
