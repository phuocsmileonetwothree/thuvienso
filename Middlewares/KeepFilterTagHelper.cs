using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;

namespace thuvienso.Middlewares
{
    /// <summary>
    /// TagHelper hỗ trợ giữ bộ lọc (Query String) trực tiếp trên View cho các tác vụ Quay lại hoặc Hủy.
    /// </summary>
    [HtmlTargetElement("a", Attributes = "keep-filter")]
    [HtmlTargetElement("form", Attributes = "keep-filter")]
    public class KeepFilterTagHelper : TagHelper
    {
        [HtmlAttributeNotBound]
        [ViewContext]
        public ViewContext ViewContext { get; set; } = null!;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            // Xóa thuộc tính keep-filter khi render ra HTML để trình duyệt sạch sẽ
            output.Attributes.RemoveAll("keep-filter");

            // Lấy bộ lọc được Filter trung tâm (Cookie bản mới) đẩy vào ViewData
            var filterQueryObj = ViewContext.ViewData["FilterQuery"];
            if (filterQueryObj == null) return;

            string filterQuery = filterQueryObj.ToString() ?? "";
            if (string.IsNullOrEmpty(filterQuery)) return;

            // 1. XỬ LÝ CHO THẺ <form keep-filter>
            if (output.TagName.Equals("form", StringComparison.OrdinalIgnoreCase))
            {
                string targetAction = output.Attributes.ContainsName("action")
                    ? output.Attributes["action"].Value?.ToString() ?? ""
                    : ViewContext.HttpContext.Request.Path.Value ?? "";

                if (!targetAction.Contains("?"))
                {
                    output.Attributes.SetAttribute("action", targetAction + filterQuery);
                }
            }

            // 2. XỬ LÝ CHO THẺ <a keep-filter> (Nút Hủy / Quay lại)
            if (output.TagName.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                if (output.Attributes.ContainsName("href"))
                {
                    string currentHref = output.Attributes["href"].Value?.ToString() ?? "";

                    if (!currentHref.Contains("?"))
                    {
                        output.Attributes.SetAttribute("href", currentHref + filterQuery);
                    }
                }
            }
        }
    }
}
