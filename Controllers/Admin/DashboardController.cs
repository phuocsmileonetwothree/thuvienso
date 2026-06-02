using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using thuvienso.Repositories;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller xử lý dữ liệu bảng điều khiển (Dashboard) trung tâm khu vực Admin.
/// Sử dụng Pattern Repository để tách biệt hoàn toàn logic truy vấn khỏi tầng Controller.
/// </summary>
[Authorize(Roles = "admin")]
[Route("admin")]
public class DashboardController : Controller
{
    private readonly DocumentRepository _docRepo;
    private readonly OrderRepository _orderRepo;

    public DashboardController(DocumentRepository docRepo, OrderRepository orderRepo)
    {
        _docRepo = docRepo;
        _orderRepo = orderRepo;
    }

    /// <summary>
    /// Tổng hợp số liệu thống kê, danh sách tài liệu hàng đầu và các đơn hàng gần đây để hiển thị lên Dashboard
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        // Khối dữ liệu tổng quan (Tổng lượt xem, lượt tải, quét QR và doanh thu)
        ViewBag.TotalViews = await _docRepo.GetTotalViewsAsync();
        ViewBag.TotalDownloads = await _docRepo.GetTotalDownloadsAsync();
        ViewBag.TotalQrScans = await _docRepo.GetTotalQrScansAsync();
        ViewBag.TotalRevenue = await _orderRepo.GetTotalRevenueAsync();

        // Khối bảng xếp hạng hiệu suất tài liệu (Top 10 theo từng tiêu chí)
        ViewBag.TopViewed = await _docRepo.GetTopViewedAsync(10);
        ViewBag.TopDownloaded = await _docRepo.GetTopDownloadedAsync(10);
        ViewBag.TopPurchased = await _docRepo.GetTopPurchasedAsync(10);
        ViewBag.TopRevenue = await _orderRepo.GetTopRevenueDocumentsAsync(10);

        // Khối danh sách lịch sử giao dịch đơn hàng mới nhất
        ViewBag.RecentOrders = await _orderRepo.GetRecentOrdersWithSummaryAsync(10);

        return View("Views/Admin/Dashboard.cshtml");
    }
}
