using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Models;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý danh sách và thống kê mã QR Code khu vực Admin.
/// Hiện tại đang tương tác trực tiếp với AppDbContext thông qua LINQ nâng cao (Eager Loading),
/// hỗ trợ bộ lọc theo tên tài liệu, loại mã QR, phân trang và sắp xếp theo số lượt quét.
/// </summary>
[Route("admin/qr")]
public class QRCodeController : Controller
{
    private readonly AppDbContext _context;

    public QRCodeController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Hiển thị danh sách mã QR Code hệ thống với các tùy chọn lọc nâng cao, phân trang và sắp xếp
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, string? type, string? sortBy, int page = 1)
    {
        int pageSize = 10;

        // Sử dụng Eager Loading để nạp kèm Document và Category tránh dính lỗi N+1 Query khi render View bồ nhé!
        var query = _context.QRCodes
            .Include(q => q.Document)
                .ThenInclude(d => d!.Category)
            .AsQueryable();

        // 1. Bộ lọc tìm kiếm theo từ khóa tên tài liệu
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(q => q.Document!.Title.Contains(search));
        }

        // 2. Bộ lọc phân loại mã QR (Ví dụ: view, download)
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse(type, out QRCodeType enumType))
        {
            query = query.Where(q => q.Type == enumType);
        }

        // 3. Xử lý sắp xếp linh hoạt (Mặc định xếp theo ID mới nhất, hoặc ưu tiên theo số lượt quét)
        query = sortBy switch
        {
            "scan" => query.OrderByDescending(q => q.ScanCount),
            _ => query.OrderByDescending(q => q.Id)
        };

        int totalItems = await query.CountAsync();

        var list = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Đẩy toàn bộ metadata phân trang và trạng thái filter ngược về View để binding UI mượt mà
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalItems = totalItems;
        ViewBag.Search = search;
        ViewBag.Type = type;
        ViewBag.SortBy = sortBy;

        return View("~/Views/Admin/QRCode/Index.cshtml", list);
    }
}
