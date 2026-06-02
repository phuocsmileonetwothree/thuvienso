using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Models;
using thuvienso.DTOs;
namespace thuvienso.Repositories
{
    public class DocumentRepository
    {
        private readonly AppDbContext _context;

        public DocumentRepository(AppDbContext context) => _context = context;

        public async Task CreateAsync(Document document) => await _context.Documents.AddAsync(document);

        public void Update(Document document) => _context.Documents.Update(document);

        public void Delete(Document document) => _context.Documents.Remove(document);

        // Tìm tài liệu theo Id gốc
        public async Task<Document?> FindByIdAsync(int id)
        {
            return await _context.Documents.FindAsync(id);
        }

        // Tìm mã QR theo DocumentId và Loại QR đang kích hoạt
        public async Task<QRCode?> FindQrCodeAsync(int documentId, QRCodeType type)
        {
            return await _context.QRCodes
                .FirstOrDefaultAsync(q => q.DocumentId == documentId && q.Type == type && q.IsActive);
        }

        // Lấy danh sách tài liệu mới nhất loại trừ tài liệu hiện tại
        public async Task<IEnumerable<Document>> GetNewestDocsAsync(int currentDocId, int limit)
        {
            return await _context.Documents
                .Where(d => d.Id != currentDocId)
                .OrderByDescending(d => d.Id)
                .Take(limit)
                .ToListAsync();
        }

        // Lấy danh sách tài liệu phổ biến theo lượt xem loại trừ tài liệu hiện tại
        public async Task<IEnumerable<Document>> GetPopularDocsAsync(int currentDocId, int limit)
        {
            return await _context.Documents
                .Where(d => d.Id != currentDocId)
                .OrderByDescending(d => d.View)
                .Take(limit)
                .ToListAsync();
        }

        // Lấy toàn bộ danh mục cho Sidebar chi tiết tài liệu
        public async Task<IEnumerable<Category>> GetAllCategoriesAsync()
        {
            return await _context.Categories.ToListAsync();
        }

        // Lấy danh sách tài liệu miễn phí mới nhất
        public async Task<IEnumerable<Document>> GetNewestFreeDocsAsync(int limit)
        {
            return await _context.Documents
                .Where(d => d.IsFree)
                .OrderByDescending(d => d.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        // Lấy danh sách tài liệu có phí mới nhất
        public async Task<IEnumerable<Document>> GetNewestPaidDocsAsync(int limit)
        {
            return await _context.Documents
                .Where(d => !d.IsFree)
                .OrderByDescending(d => d.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        // Lấy danh sách tài liệu ngẫu nhiên
        public async Task<IEnumerable<Document>> GetRandomDocsAsync(int limit)
        {
            return await _context.Documents
                .OrderBy(d => Guid.NewGuid())
                .Take(limit)
                .ToListAsync();
        }

        // Lấy danh mục nhiều tài liệu nhất kèm danh sách tài liệu giới hạn chạy 1 câu lệnh SQL
        public async Task<List<Category>> GetTopCategoriesWithDocsAsync(int categoryLimit, int docLimit)
        {
            return await _context.Categories
                .Where(c => c.Documents.Any())
                .OrderByDescending(c => c.Documents.Count)
                .Take(categoryLimit)
                .Select(c => new Category
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    ParentId = c.ParentId,
                    Documents = c.Documents
                        .OrderByDescending(d => d.CreatedAt)
                        .Take(docLimit)
                        .ToList()
                })
                .ToListAsync();
        }

        // Bộ lọc nâng cao và phân trang tài liệu chạy 100% dưới MySQL
        public async Task<DocumentFilterResponse<Document>> GetPagedDocumentsAsync(DocumentFilterParams p)
        {
            IQueryable<Document> query;
            if (!string.IsNullOrWhiteSpace(p.Search))
            {
                query = _context.Documents
                    .FromSqlRaw(@"SELECT * FROM Documents WHERE MATCH (Title) AGAINST ({0} IN NATURAL LANGUAGE MODE)", p.Search)
                    .AsQueryable();
            }
            else
            {
                query = _context.Documents.AsQueryable();
            }

            if (p.CategoryId.HasValue) query = query.Where(d => d.CategoryId == p.CategoryId);
            if (p.PublisherId.HasValue) query = query.Where(d => d.PublisherId == p.PublisherId);
            if (p.AuthorId.HasValue) query = query.Where(d => d.DocumentAuthors.Any(da => da.AuthorId == p.AuthorId));

            query = p.Sort switch
            {
                "newest" => query.OrderByDescending(d => d.Id),
                "oldest" => query.OrderBy(d => d.Id),
                "most_viewed" => query.OrderByDescending(d => d.View ?? 0),
                "most_downloaded" => query.OrderByDescending(d => d.Download ?? 0),
                "free" => query.Where(d => d.IsFree).OrderByDescending(d => d.Id),
                "fee" => query.Where(d => !d.IsFree).OrderByDescending(d => d.Id),
                _ => query.OrderByDescending(d => d.Id)
            };

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)p.PageSize);

            var items = await query
                .Skip((p.Page - 1) * p.PageSize)
                .Take(p.PageSize)
                .ToListAsync();

            return new DocumentFilterResponse<Document> { Items = items, TotalCount = totalCount, TotalPages = totalPages };
        }

        // Bổ sung thêm các hàm lấy nhanh danh sách đổ vào Dropdown
        public async Task<string?> GetCategoryNameAsync(int id) =>
            (await _context.Categories.FindAsync(id))?.Name;

        public async Task<IEnumerable<SelectListItem>> GetCategorySelectListAsync() =>
            await _context.Categories.Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync();

        public async Task<IEnumerable<SelectListItem>> GetPublisherSelectListAsync() =>
            await _context.Publishers.Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name }).ToListAsync();

        public async Task<IEnumerable<SelectListItem>> GetAuthorSelectListAsync() =>
            await _context.Authors.Select(a => new SelectListItem { Value = a.Id.ToString(), Text = a.Name }).ToListAsync();

        // =================================================================
        // HÀM THỐNG KÊ ĐỒNG BỘ DÀNH CHO ADMIN DASHBOARD

        // Tính tổng lượt xem của tất cả tài liệu
        public async Task<int> GetTotalViewsAsync() =>
            await _context.Documents.SumAsync(d => d.View ?? 0);

        // Tính tổng lượt tải của tất cả tài liệu
        public async Task<int> GetTotalDownloadsAsync() =>
            await _context.Documents.SumAsync(d => d.Download ?? 0);

        // Tính tổng lượt quét mã QR toàn hệ thống
        public async Task<int> GetTotalQrScansAsync() =>
            await _context.QRCodes.SumAsync(q => q.ScanCount);

        // Lấy top tài liệu có lượt xem cao nhất
        public async Task<IEnumerable<Document>> GetTopViewedAsync(int limit = 10)
        {
            return await _context.Documents
                .OrderByDescending(d => d.View)
                .Take(limit)
                .ToListAsync();
        }

        // Lấy top tài liệu có lượt tải cao nhất
        public async Task<IEnumerable<Document>> GetTopDownloadedAsync(int limit = 10)
        {
            return await _context.Documents
                .OrderByDescending(d => d.Download)
                .Take(limit)
                .ToListAsync();
        }

        // Lấy top tài liệu có lượt mua nhiều nhất
        public async Task<List<Document>> GetTopPurchasedAsync(int limit = 10)
        {
            return await _context.Documents
                .AsNoTracking() 
                .OrderByDescending(d => d.Purchase)
                .Take(limit)
                .Select(d => new Document
                {
                    Id = d.Id,
                    Title = d.Title,
                    Purchase = d.Purchase
                })
                .ToListAsync();
        }
        // =================================================================

        //=====================================================
        // 📚 BỘ LỌC NÂNG CAO VÀ PHÂN TRANG DÀNH CHO ADMIN - DOCUMENT CONTROLLER DÙNG
        public async Task<(IEnumerable<Document> Items, int TotalItems)> GetAdminPagedDocumentsAsync(
            string? search, bool? isFree, int? categoryId, int? publisherId, List<int>? authorIds, string? sortBy, int page, int pageSize)
        {
            var query = _context.Documents
                .Include(d => d.Category)
                .Include(d => d.Publisher)
                .Include(d => d.DocumentAuthors).ThenInclude(da => da.Author)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(d => d.Title.Contains(search));

            if (isFree.HasValue)
                query = query.Where(d => d.IsFree == isFree.Value);

            if (categoryId.HasValue && categoryId > 0)
                query = query.Where(d => d.CategoryId == categoryId);

            if (publisherId.HasValue && publisherId > 0)
                query = query.Where(d => d.PublisherId == publisherId);

            if (authorIds != null && authorIds.Any())
                query = query.Where(d => d.DocumentAuthors.Any(da => authorIds.Contains(da.AuthorId)));

            query = sortBy switch
            {
                "view" => query.OrderByDescending(d => d.View),
                "download" => query.OrderByDescending(d => d.Download),
                "purchase" => query.OrderByDescending(d => d.Purchase),
                _ => query.OrderByDescending(d => d.Id)
            };

            int totalItems = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalItems);
        }

        // Tìm kiếm Document kèm đầy đủ bảng quan hệ để Edit
        public async Task<Document?> FindWithAuthorsByIdAsync(int id)
        {
            return await _context.Documents
                .Include(d => d.DocumentAuthors)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        // Lấy danh sách thuần để điền vào Dropdown/Select cụ thể
        public async Task<IEnumerable<Category>> GetRawCategoriesAsync() => await _context.Categories.ToListAsync();
        public async Task<IEnumerable<Publisher>> GetRawPublishersAsync() => await _context.Publishers.ToListAsync();
        public async Task<IEnumerable<Author>> GetRawAuthorsAsync() => await _context.Authors.ToListAsync();

        // Thêm nhanh QRCode con
        public async Task AddQrCodeAsync(QRCode qrCode) => await _context.QRCodes.AddAsync(qrCode);

        // Xóa danh sách QRCode theo DocumentId
        public async Task DeleteQrsByDocIdAsync(int documentId)
        {
            var qrs = await _context.QRCodes.Where(q => q.DocumentId == documentId).ToListAsync();
            if (qrs.Any()) _context.QRCodes.RemoveRange(qrs);
        }

        // Quản lý gán tác giả liên kết
        public void AddDocumentAuthor(DocumentAuthor docAuthor) => _context.DocumentAuthors.Add(docAuthor);
        public void RemoveDocumentAuthors(IEnumerable<DocumentAuthor> docAuthors) => _context.DocumentAuthors.RemoveRange(docAuthors);
        //=====================================================

        public async Task<bool> SaveAsync() => await _context.SaveChangesAsync() > 0;
    }
}
