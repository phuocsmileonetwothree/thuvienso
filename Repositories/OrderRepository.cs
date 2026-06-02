using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Models;

namespace thuvienso.Repositories
{
    public class OrderRepository
    {
        private readonly AppDbContext _context;

        public OrderRepository(AppDbContext context) => _context = context;

        //=============================================

        // Tạo mới Order gốc
        public async Task CreateAsync(Order order) => await _context.Orders.AddAsync(order);

        // Cập nhật Order
        public void Update(Order order) => _context.Orders.Update(order);

        // Xóa Order gốc nếu cần
        public void Delete(Order order) => _context.Orders.Remove(order);

        // Tạo mới OrderDetail
        public async Task CreateDetailAsync(OrderDetail detail) => await _context.OrderDetails.AddAsync(detail);

        // Cập nhật OrderDetail
        public void UpdateDetail(OrderDetail detail) => _context.OrderDetails.Update(detail);

        // Xoá OrderDetail
        public void DeleteDetail(OrderDetail detail) => _context.OrderDetails.Remove(detail);

        //================================================

        // Tìm Order theo Id
        public async Task<Order?> FindByIdAsync(int id)
        {
            return await _context.Orders.FindAsync(id);
        }

        // Tìm Order theo Id. Phục vụ cho Hangfire
        public async Task<Order?> FindByIdForFulfillmentAsync(int id)
        {
            return await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Document)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        // Tìm Order theo cặp UserId và DocumentId
        public async Task<Order?> FindByPairAsync(int userId, int documentId)
        {
            return await _context.Orders
                .FirstOrDefaultAsync(o => o.UserId == userId && o.DocumentId == documentId);
        }

        // Tìm OrderDetail theo OrderCode
        public async Task<OrderDetail?> FindDetailByCodeAsync(string orderCode)
        {
            return await _context.OrderDetails
                .FirstOrDefaultAsync(od => od.OrderCode == orderCode);
        }

        // Tìm OrderDetail theo OrderCode. Hàm phục vụ riêng cho Hangfire Job
        public async Task<OrderDetail?> FindDetailByCodeForHangfireAsync(string orderCode)
        {
            return await _context.OrderDetails
                .Include(od => od.Order)
                    .ThenInclude(o => o.User)
                .Include(od => od.Order)
                    .ThenInclude(o => o.Document)
                .FirstOrDefaultAsync(od => od.OrderCode == orderCode);
        }

        // Tìm OrderDetail theo PaymentLinkId
        public async Task<OrderDetail?> FindDetailByLinkIdAsync(string paymentLinkId)
        {
            return await _context.OrderDetails
                .FirstOrDefaultAsync(od => od.PaymentLinkId == paymentLinkId);
        }

        // Tìm OrderDetail MỚI NHẤT theo Id của Order
        public async Task<OrderDetail?> FindLatestDetailByOrderIdAsync(int orderId)
        {
            return await _context.OrderDetails
                .Where(od => od.OrderId == orderId)
                .OrderByDescending(od => od.Id)
                .FirstOrDefaultAsync();
        }

        // Tìm OrderDetail MỚI NHẤT theo cặp UserId và DocumentId
        public async Task<OrderDetail?> FindLatestDetailByPairAsync(int userId, int documentId)
        {
            return await _context.OrderDetails
                .Where(od => od.Order.UserId == userId && od.Order.DocumentId == documentId)
                .OrderByDescending(od => od.Id)
                .FirstOrDefaultAsync();
        }

        // Tìm OrderDetail MỚI NHẤT theo cặp UserId và DocumentId. Hàm phục vụ riêng cho Hangfire Job
        public async Task<OrderDetail?> FindLatestDetailByPairForHangfireAsync(int userId, int documentId)
        {
            return await _context.OrderDetails
                .Include(od => od.Order)
                    .ThenInclude(o => o.User)
                .Include(od => od.Order)
                    .ThenInclude(o => o.Document)
                .Where(od => od.Order.UserId == userId && od.Order.DocumentId == documentId)
                .OrderByDescending(od => od.Id)
                .FirstOrDefaultAsync();
        }

        // Tìm OrderDetail ĐÃ THANH TOÁN + MỚI NHẤT theo cặp UserId và DocumentId
        public async Task<OrderDetail?> FindLatestPaidDetailByPairAsync(int userId, int documentId)
        {
            return await _context.OrderDetails
                .Where(od => od.Status == OrderStatus.paid &&
                             od.Order.UserId == userId &&
                             od.Order.DocumentId == documentId)
                .OrderByDescending(od => od.Id)
                .FirstOrDefaultAsync();
        }

        // Lấy danh sách OrderDetail theo Id của Order
        public async Task<IEnumerable<OrderDetail>> GetDetailsByOrderIdAsync(int orderId)
        {
            return await _context.OrderDetails
                .Where(od => od.OrderId == orderId)
                .OrderByDescending(od => od.CreatedAt)
                .ToListAsync();
        }

        // Lấy danh sách OrderDetail ĐÃ THANH TOÁN + MỚI NHẤT của từng tài liệu thuộc sở hữu của User
        public async Task<IEnumerable<OrderDetail>> GetLatestPaidDetailsByUserIdAsync(int userId)
        {
            return await _context.OrderDetails
                .Where(od => od.Status == OrderStatus.paid && od.Order.UserId == userId)
                .GroupBy(od => od.Order.DocumentId)
                .Select(g => g.OrderByDescending(od => od.PercentPaid)
                              .ThenByDescending(od => od.Id)
                              .First())
                .ToListAsync();
        }

        // Lấy danh sách OrderDetail CHỜ THANH TOÁN của User
        public async Task<IEnumerable<OrderDetail>> GetPendingDetailsByUserIdAsync(int userId)
        {
            return await _context.OrderDetails
                .Where(od => od.Status == OrderStatus.pending && od.Order.UserId == userId)
                .OrderByDescending(od => od.CreatedAt)
                .ToListAsync();
        }

        //=====================================================
        // Tính tổng doanh thu thực tế từ các giao dịch con thành công
        public async Task<decimal> GetTotalRevenueAsync()
        {
            return await _context.OrderDetails
                .Where(d => d.Status == OrderStatus.paid)
                .SumAsync(d => d.PricePaid);
        }

        // Lấy danh sách tài liệu có doanh thu cao nhất
        public async Task<List<dynamic>> GetTopRevenueDocumentsAsync(int limit = 10)
        {
            var topRevenue = await _context.OrderDetails
                .Where(d => d.Status == OrderStatus.paid)
                .GroupBy(d => d.Order.DocumentId)
                .Select(g => new {
                    DocumentId = g.Key,
                    Revenue = g.Sum(x => x.PricePaid)
                })
                .OrderByDescending(g => g.Revenue)
                .Take(limit)
                .Join(_context.Documents, g => g.DocumentId, d => d.Id,
                    (g, d) => new { d.Id, d.Title, Revenue = g.Revenue })
                .ToListAsync();

            return topRevenue.Cast<dynamic>().ToList();
        }

        // Lấy danh sách các đơn hàng gần đây kèm thông tin chi tiết tổng hợp
        public async Task<List<dynamic>> GetRecentOrdersWithSummaryAsync(int limit = 10)
        {
            var recentOrders = await _context.Orders
                .OrderByDescending(o => o.Id)
                .Take(limit)
                .Select(o => new
                {
                    Id = o.Id,
                    PaymentStatus = o.OrderDetails.OrderByDescending(d => d.Id).Select(d => d.Status).FirstOrDefault(),
                    PercentPaid = o.OrderDetails.Where(d => d.Status == OrderStatus.paid).Select(d => (decimal?)d.PercentPaid).Max() ?? 0,
                    TotalPrice = o.TotalPrice,
                    PricePaid = o.OrderDetails.Where(d => d.Status == OrderStatus.paid).Sum(d => d.PricePaid),
                    TransactionTime = o.OrderDetails.Where(d => d.Status == OrderStatus.paid).OrderByDescending(d => d.TransactedAt).Select(d => d.TransactedAt).FirstOrDefault() ?? o.UpdatedAt,
                    CreatedAt = o.CreatedAt,
                    UserFullName = o.User.Name,
                    Phone = o.User.Phone,
                    Email = o.User.Email,
                    DocumentTitle = o.Document.Title
                })
                .ToListAsync();

            return recentOrders.Cast<dynamic>().ToList();
        }
        //=====================================================

        //=====================================================
        // BỘ LỌC NÂNG CAO VÀ PHÂN TRANG DÀNH CHO ADMIN
        // Phân trang & Tìm danh sách Order
        public async Task<(List<dynamic> Items, int TotalItems)> GetPagedOrdersAsync(
            string? search, DateTime? fromDate, DateTime? toDate, int page, int pageSize)
        {
            var query = _context.Orders.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(o =>
                    o.User.Name.Contains(search) ||
                    o.User.Email.Contains(search) ||
                    o.User.Phone.Contains(search) ||
                    o.Document.Title.Contains(search) ||
                    o.OrderDetails.Any(d => d.OrderCode.Contains(search))
                );
            }

            if (fromDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt >= fromDate);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt <= toDate.Value.AddDays(1).AddTicks(-1));
            }

            int totalItems = await query.CountAsync();

            var items = await query
                .OrderByDescending(o => o.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => new
                {
                    Id = o.Id,
                    UserFullName = o.User.Name,
                    Email = o.User.Email,
                    Phone = o.User.Phone,
                    DocumentId = o.DocumentId,
                    DocumentTitle = o.Document.Title,
                    TotalPrice = o.TotalPrice,
                    CreatedAt = o.CreatedAt,
                })
                .ToListAsync();

            return (items.Cast<dynamic>().ToList(), totalItems);
        }

        // Phân trang & Tìm danh sách OrderDetail của Order
        public async Task<(IEnumerable<OrderDetail> Items, int TotalItems)> GetPagedOrderDetailsAsync(
            int orderId, string? search, OrderStatus? status, decimal? percent, DateTime? fromDate, DateTime? toDate, int page, int pageSize)
        {
            var query = _context.OrderDetails
                .Include(d => d.Order).ThenInclude(o => o.User)
                .Include(d => d.Order).ThenInclude(o => o.Document)
                .Where(d => d.OrderId == orderId)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(d =>
                    d.Order.User.Name.Contains(search) ||
                    d.Order.User.Email.Contains(search) ||
                    d.Order.User.Phone.Contains(search) ||
                    d.Order.Document.Title.Contains(search) ||
                    d.OrderCode.Contains(search)
                );
            }

            if (status.HasValue)
            {
                query = query.Where(d => d.Status == status.Value);
            }

            if (percent.HasValue)
            {
                query = query.Where(d => d.PercentPaid == percent.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(d => d.CreatedAt >= fromDate);
            }

            if (toDate.HasValue)
            {
                query = query.Where(d => d.CreatedAt <= toDate.Value.AddDays(1).AddTicks(-1));
            }

            int totalItems = await query.CountAsync();
            var items = await query
                .OrderByDescending(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalItems);
        }
        //=====================================================

        // Lưu thay đổi xuống Database
        public async Task<bool> SaveAsync() => await _context.SaveChangesAsync() > 0;
    }
}
