using Microsoft.AspNetCore.Mvc;
using thuvienso.Repositories;
using thuvienso.DTOs;

namespace thuvienso.Controllers.User
{
    [Route("category")]
    public class CategoryController : Controller
    {
        private readonly DocumentRepository _documentRepo;

        public CategoryController(DocumentRepository documentRepo)
        {
            _documentRepo = documentRepo;
        }

        /// <summary>
        /// Hiển thị danh sách tài liệu theo bộ lọc động và dữ liệu phân trang.
        /// </summary>
        [HttpGet("")]
        public async Task<IActionResult> Index(string? search, int? categoryId, int? publisherId, int? authorId, string? sort, int page = 1)
        {
            // 1. Đóng gói tham số lọc vào DTO
            var filterParams = new DocumentFilterParams
            {
                Search = search,
                CategoryId = categoryId,
                PublisherId = publisherId,
                AuthorId = authorId,
                Sort = sort,
                Page = page
            };

            // 2. Gọi Repo lấy kết quả phân trang động
            var pagedResult = await _documentRepo.GetPagedDocumentsAsync(filterParams);

            // 3. Xử lý hiển thị tên danh mục hiện tại lên UI
            if (categoryId.HasValue)
            {
                var categoryName = await _documentRepo.GetCategoryNameAsync(categoryId.Value);
                ViewBag.CurrentCategoryName = categoryName ?? $"Danh mục không tồn tại (ID: {categoryId})";
            }
            else
            {
                ViewBag.CurrentCategoryName = "Danh mục tài liệu";
            }

            // 4. Đồng bộ các thông số phân trang ra View
            ViewBag.TotalPages = pagedResult.TotalPages;
            ViewBag.CurrentPage = page;

            // 5. Nạp dữ liệu cho hệ thống Dropdown bộ lọc
            ViewBag.Categories = await _documentRepo.GetCategorySelectListAsync();
            ViewBag.Publishers = await _documentRepo.GetPublisherSelectListAsync();
            ViewBag.Authors = await _documentRepo.GetAuthorSelectListAsync();

            return View("~/Views/User/Category/Index.cshtml", pagedResult.Items);
        }
    }
}
