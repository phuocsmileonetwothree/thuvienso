using Microsoft.AspNetCore.Mvc;
using thuvienso.Repositories;

namespace thuvienso.Controllers.User
{
    [Route("user/profile")]
    public class ProfileController : Controller
    {
        private readonly OrderRepository _orderRepo;

        public ProfileController(OrderRepository orderRepo)
        {
            _orderRepo = orderRepo;
        }

        /// <summary>
        /// Thu thập danh sách tài liệu đã sở hữu và các giao dịch đang chờ xử lý để hiển thị giao diện hồ sơ cá nhân.
        /// </summary>
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Redirect("/user/auth/login");

            // TAB 1: Gọi hàm sạch từ Repo lấy các tài liệu đã paid sở hữu cao nhất
            var paidDetails = await _orderRepo.GetLatestPaidDetailsByUserIdAsync(userId.Value);

            // TAB 2: Lấy danh sách các giao dịch con đang chờ thanh toán (Pending)
            var pendingDetails = await _orderRepo.GetPendingDetailsByUserIdAsync(userId.Value);

            ViewBag.PendingDocs = pendingDetails;

            // Truyền list OrderDetail sang cho View nhận
            return View("~/Views/User/Profile.cshtml", paidDetails);
        }
    }
}
