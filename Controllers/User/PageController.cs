using Microsoft.AspNetCore.Mvc;

namespace thuvienso.Controllers.User
{
    [Route("page")]
    public class PageController : Controller
    {
        /// <summary>
        /// Hiển thị giao diện trang chủ mặc định của phân vùng trang tĩnh.
        /// </summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>
        /// Hiển thị giao diện giới thiệu về nền tảng Thư Viện Số.
        /// </summary>
        [HttpGet("about")]
        public IActionResult About()
        {
            return View("~/Views/User/Page/About.cshtml");
        }
    }
}
