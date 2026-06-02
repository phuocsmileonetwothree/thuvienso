using Microsoft.AspNetCore.Mvc;
using thuvienso.Models;
using thuvienso.Repositories;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller quản lý danh sách đơn hàng tổng và chi tiết hóa đơn con (OrderDetail) khu vực Admin.
/// Tận dụng tối đa Pattern Repository để tối ưu bộ lọc tìm kiếm nâng cao và phân trang dữ liệu.
/// </summary>
[Route("admin/order")]
public class OrderController : Controller
{
    private readonly OrderRepository _orderRepo;

    public OrderController(OrderRepository orderRepo)
    {
        _orderRepo = orderRepo;
    }

    /// <summary>
    /// Hiển thị danh sách đơn hàng tổng (Master Orders) kèm bộ lọc đa điều kiện và phân trang
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        int pageSize = 10;

        // Fetch dữ liệu phẳng (dynamic) đã xử lý từ Repo
        var (orders, totalItems) = await _orderRepo.GetPagedOrdersAsync(
            search, fromDate, toDate, page, pageSize);

        ViewBag.Orders = orders;

        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalItems = totalItems;
        ViewBag.Search = search;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        return View("~/Views/Admin/Order/Index.cshtml");
    }

    /// <summary>
    /// Xem chi tiết danh sách hóa đơn con (OrderDetails) thuộc một mã đơn hàng tổng xác định.
    /// Hỗ trợ tìm kiếm, lọc trạng thái nội bộ và phân trang trên danh mục sản phẩm của đơn hàng.
    /// </summary>
    /// <param name="orderId">Mã định danh duy nhất của đơn hàng tổng</param>
    [HttpGet("{orderId:int}/transactions")]
    public async Task<IActionResult> Details(int orderId, string? search, OrderStatus? status, decimal? percent, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        int pageSize = 10;

        // 1. Truy vấn danh sách chi tiết các hóa đơn con (OrderDetail) đã áp dụng bộ lọc và phân trang
        var (details, totalItems) = await _orderRepo.GetPagedOrderDetailsAsync(
            orderId, search, status, percent, fromDate, toDate, page, pageSize);

        // 2. Lấy thông tin thực thể đơn hàng tổng để hiển thị thông tin chung (Mã hóa đơn, người mua, tổng tiền) trên tiêu đề View
        var mainOrder = await _orderRepo.FindByIdAsync(orderId);

        ViewBag.OrderId = orderId;
        ViewBag.MainOrder = mainOrder;
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalItems = totalItems;
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.Percent = percent;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        return View("~/Views/Admin/Order/Detail.cshtml", details);
    }
}
