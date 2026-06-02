using Microsoft.AspNetCore.Mvc;
using thuvienso.Helpers;
using thuvienso.Repositories;

namespace thuvienso.Controllers.User
{
    [Route("")]
    public class HomeController : Controller
    {
        private readonly DocumentRepository _documentRepo;

        // Tiêm Repo trực tiếp, không qua Service vì luồng này chỉ đọc dữ liệu
        public HomeController(DocumentRepository documentRepo)
        {
            _documentRepo = documentRepo;
        }

        /// <summary>
        /// Thu thập và phân loại các danh sách tài liệu nổi bật, cấu trúc danh mục để hiển thị trên trang chủ.
        /// </summary>
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            // Các hàm cũ đã có sẵn trong Repo (truyền 0 để không bỏ qua tài liệu nào)
            ViewBag.Newest = await _documentRepo.GetNewestDocsAsync(currentDocId: 0, limit: 12);
            ViewBag.MostViewed = await _documentRepo.GetPopularDocsAsync(currentDocId: 0, limit: 12);

            // Các hàm mới tối ưu hóa phân loại
            ViewBag.Free = await _documentRepo.GetNewestFreeDocsAsync(limit: 12);
            ViewBag.Paid = await _documentRepo.GetNewestPaidDocsAsync(limit: 12);
            ViewBag.Random = await _documentRepo.GetRandomDocsAsync(limit: 12);

            // Danh mục phẳng
            var allCategories = await _documentRepo.GetAllCategoriesAsync();
            ViewBag.FlatCategories = CategoryHelper.BuildTree(allCategories.ToList());

            // Tối ưu hóa luồng bốc Top 3 Categories kèm 6 Docs mới nhất
            var topCategories = await _documentRepo.GetTopCategoriesWithDocsAsync(categoryLimit: 3, docLimit: 6);

            ViewBag.CategoryTop1View = topCategories.ElementAtOrDefault(0);
            ViewBag.CategoryTop2View = topCategories.ElementAtOrDefault(1);
            ViewBag.CategoryTop3View = topCategories.ElementAtOrDefault(2);

            return View("~/Views/User/Home.cshtml");
        }
    }
}
