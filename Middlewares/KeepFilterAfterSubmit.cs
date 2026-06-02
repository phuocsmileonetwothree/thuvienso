using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace thuvienso.Middlewares
{
    public class KeepFilterAfterSubmit : IActionFilter, IResultFilter
    {
        private const string CookiePrefix = "AdminFilter_";

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;
            var request = httpContext.Request;
            var controller = context.Controller as Controller;
            if (controller == null) return;

            string controllerName = context.RouteData.Values["controller"]?.ToString() ?? "";
            string actionName = context.RouteData.Values["action"]?.ToString() ?? "";
            string cookieName = CookiePrefix + controllerName.ToLower();

            string currentQuery = request.QueryString.Value ?? "";

            // 🎯 BƯỚC 1: Sử dụng Action Name thay vì ép chuỗi URL trần để tránh lỗi trùng tên
            if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                actionName.Equals("Index", StringComparison.OrdinalIgnoreCase))
            {
                var cookieOptions = new CookieOptions { Expires = DateTimeOffset.UtcNow.AddHours(1), HttpOnly = true };
                httpContext.Response.Cookies.Append(cookieName, currentQuery, cookieOptions);
                controller.ViewData["FilterQuery"] = currentQuery;
            }
            // 🎯 BƯỚC 2: Các trang con (Create, Edit, Delete...)
            else
            {
                string savedQuery = request.Cookies[cookieName] ?? "";
                controller.ViewData["FilterQuery"] = savedQuery;
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        public void OnResultExecuting(ResultExecutingContext context)
        {
            var httpContext = context.HttpContext;

            if (context.Result is RedirectToActionResult redirectToAction)
            {
                if (redirectToAction.ActionName.Equals("Index", StringComparison.OrdinalIgnoreCase))
                {
                    string controllerName = context.RouteData.Values["controller"]?.ToString() ?? "";
                    string actionName = context.RouteData.Values["action"]?.ToString() ?? "";
                    string cookieName = CookiePrefix + controllerName.ToLower();

                    // 🚨 LUỒNG TẠO MỚI (CREATE): Dựa trên Action hiện tại để xóa sạch bộ lọc
                    if (actionName.Equals("Create", StringComparison.OrdinalIgnoreCase))
                    {
                        httpContext.Response.Cookies.Delete(cookieName);
                        return;
                    }

                    // 🚨 LUỒNG XÓA/SỬA: Chỉ dán đuôi nếu quay về Index của CHÍNH Controller đó
                    var targetController = redirectToAction.ControllerName ?? controllerName;
                    if (targetController.Equals(controllerName, StringComparison.OrdinalIgnoreCase))
                    {
                        string filterQuery = httpContext.Request.Cookies[cookieName] ?? "";
                        if (!string.IsNullOrEmpty(filterQuery))
                        {
                            string targetUrl = $"/admin/{targetController}".ToLower();
                            context.Result = new LocalRedirectResult(targetUrl + filterQuery);
                        }
                    }
                }
            }
            else if (context.Result is RedirectResult redirectResult)
            {
                string url = redirectResult.Url;
                string controllerName = context.RouteData.Values["controller"]?.ToString() ?? "";
                string cookieName = CookiePrefix + controllerName.ToLower();
                string filterQuery = httpContext.Request.Cookies[cookieName] ?? "";

                if (!string.IsNullOrEmpty(filterQuery))
                {
                    // Xử lý thông minh: Nếu URL chuyển hướng có sẵn dấu `?`, dùng `&`, nếu chưa thì dùng `?`
                    string separator = url.Contains("?") ? "&" : "";

                    // Nếu URL đã chứa tham số, ta bóc bỏ dấu `?` ở đầu filterQuery đi để nối chuỗi
                    if (!string.IsNullOrEmpty(separator) && filterQuery.StartsWith("?"))
                    {
                        filterQuery = filterQuery.Substring(1);
                    }

                    context.Result = new LocalRedirectResult(url + separator + filterQuery);
                }
            }
        }

        public void OnResultExecuted(ResultExecutedContext context) { }
    }
}
