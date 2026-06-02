using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Models;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý danh sách và bộ lọc các liên hệ gửi từ người dùng tại khu vực Admin
/// </summary>
[Route("admin/contact")]
public class ContactController : Controller
{
    private readonly AppDbContext _context;

    public ContactController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Tìm kiếm nâng cao, lọc theo khoảng thời gian và phân trang danh sách liên hệ
    /// </summary>
    /// <param name="name">Lọc theo tên người gửi</param>
    /// <param name="email">Lọc theo địa chỉ email</param>
    /// <param name="phone">Lọc theo số điện thoại</param>
    /// <param name="isHandled">Trạng thái xử lý liên hệ (Đang tạm đóng logic lọc)</param>
    /// <param name="fromDate">Mốc thời gian bắt đầu</param>
    /// <param name="toDate">Mốc thời gian kết thúc</param>
    /// <param name="page">Số trang hiện tại (mặc định là 1)</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? name, string? email, string? phone, string? isHandled, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        int pageSize = 10;
        var query = _context.Contacts.AsQueryable();

        // Áp dụng các điều kiện lọc chuỗi văn bản nếu có dữ liệu đầu vào
        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(c => c.Name.Contains(name));

        if (!string.IsNullOrWhiteSpace(email))
            query = query.Where(c => c.Email.Contains(email));

        if (!string.IsNullOrWhiteSpace(phone))
            query = query.Where(c => c.Phone.Contains(phone));

        // Logic lọc trạng thái xử lý (Hiện tại đang comment, không ảnh hưởng đến truy vấn)
        if (!string.IsNullOrEmpty(isHandled))
        {
            bool handled = isHandled == "true";
            //query = query.Where(c => c.IsHandled == handled);
        }

        // Lọc dữ liệu theo khoảng thời gian tạo (CreatedAt)
        if (fromDate.HasValue)
            query = query.Where(c => c.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(c => c.CreatedAt <= toDate.Value);

        int totalItems = await query.CountAsync();

        // Thực hiện phân trang và sắp xếp bản ghi mới nhất lên đầu
        var contacts = await query
            .OrderByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Đồng bộ dữ liệu bộ lọc quay ngược lại View để giữ nguyên trạng thái dữ liệu trên Form
        ViewBag.Name = name;
        ViewBag.Email = email;
        ViewBag.Phone = phone;
        //ViewBag.IsHandled = isHandled;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalItems = totalItems;

        return View("~/Views/Admin/Contact/Index.cshtml", contacts);
    }

    /// <summary>
    /// Xóa bản ghi liên hệ dựa trên ID và có cơ chế bắt ngoại lệ (Try-Catch) an toàn
    /// </summary>
    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var contact = await _context.Contacts.FindAsync(id);
        if (contact == null)
        {
            TempData["Error"] = "Không tìm thấy liên hệ cần xoá.";
            return RedirectToAction("Index");
        }

        try
        {
            _context.Contacts.Remove(contact);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Đã xoá liên hệ thành công.";
        }
        catch
        {
            // Ngăn chặn crash ứng dụng nếu xảy ra lỗi xung đột dữ liệu hoặc mất kết nối DB
            TempData["Error"] = "Xảy ra lỗi khi xoá liên hệ.";
        }

        return RedirectToAction("Index");
    }
}
