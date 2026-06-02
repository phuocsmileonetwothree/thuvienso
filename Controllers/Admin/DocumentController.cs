
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using QRCoder;
using thuvienso.Models;
using thuvienso.Repositories;
using thuvienso.Services;

// Các thư viện xử lý ảnh và PDF
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

// Alias tránh xung đột namespace giữa các thư viện PDF
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;
using PdfPigBuilder = UglyToad.PdfPig.Writer.PdfDocumentBuilder;
using PdfiumDoc = PdfiumViewer.PdfDocument;

namespace thuvienso.Controllers.Admin;

/// <summary>
/// Controller xử lý nghiệp vụ quản lý Tài liệu (Documents) nâng cao khu vực Admin.
/// Bao gồm: CRUD dữ liệu, cắt PDF (Preview, 25%, 50%), render Thumbnail, tích hợp QR Code và chạy ngầm qua Hangfire.
/// </summary>
[Route("admin/document")]
public class DocumentController : Controller
{
    private readonly DocumentRepository _docRepo;

    public DocumentController(DocumentRepository docRepo)
    {
        _docRepo = docRepo;
    }

    #region HÀM HỖ TRỢ XỬ LÝ FILE (I/O)

    /// <summary>
    /// Render trang đầu tiên của file PDF làm ảnh đại diện (Thumbnail).
    /// Nếu lỗi, hệ thống tự động sinh một ảnh xám (Placeholder) làm fallback để tránh crash.
    /// </summary>
    public async Task RenderPdfThumbnail(string inputPdf, string outputJpg)
    {
        await Task.Run(() =>
        {
            try
            {
                using (var document = PdfiumDoc.Load(inputPdf))
                using (var image = document.Render(0, 300, 400, true))
                {
                    image.Save(outputJpg, System.Drawing.Imaging.ImageFormat.Jpeg);
                }
            }
            catch
            {
                // Fallback: Tạo ảnh placeholder xám nếu PDF bị mã hóa hoặc lỗi định dạng
                using (var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(300, 400))
                {
                    image.Mutate(x => x.BackgroundColor(Color.Gray));
                    image.SaveAsJpeg(outputJpg);
                }
            }
        });
    }

    /// <summary>
    /// Trích xuất N trang đầu tiên (mặc định là 3 trang) của tài liệu PDF gốc để làm bản xem trước công khai
    /// </summary>
    private bool GeneratePreviewPdf(string sourcePath, string previewPath, int maxPages = 3)
    {
        try
        {
            using (var document = PdfPigDoc.Open(sourcePath))
            {
                var builder = new PdfPigBuilder();
                int count = Math.Min(maxPages, document.NumberOfPages);
                for (int i = 1; i <= count; i++)
                {
                    builder.AddPage(document, i);
                }
                System.IO.File.WriteAllBytes(previewPath, builder.Build());
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cắt đồng thời file PDF gốc thành 2 phiên bản giới hạn: 25% và 50% tổng số trang.
    /// Hàm xử lý tĩnh (static) giúp tối ưu hóa RAM và phục vụ tốt cho tác vụ chạy ngầm.
    /// </summary>
    public static bool GeneratePercentPdfsCombined(string sourcePath, string target25Path, string target50Path)
    {
        try
        {
            using (var document = PdfPigDoc.Open(sourcePath))
            {
                int totalPages = document.NumberOfPages;
                if (totalPages == 0) return false;

                int pagesFor25 = (int)Math.Ceiling((double)totalPages * 25 / 100);
                int pagesFor50 = (int)Math.Ceiling((double)totalPages * 50 / 100);

                // Xử lý tạo phiên bản 25% số trang
                var builder25 = new PdfPigBuilder();
                for (int i = 1; i <= Math.Min(pagesFor25, totalPages); i++)
                {
                    builder25.AddPage(document, i);
                }
                System.IO.File.WriteAllBytes(target25Path, builder25.Build());

                // Xử lý tạo phiên bản 50% số trang
                var builder50 = new PdfPigBuilder();
                for (int i = 1; i <= Math.Min(pagesFor50, totalPages); i++)
                {
                    builder50.AddPage(document, i);
                }
                System.IO.File.WriteAllBytes(target50Path, builder50.Build());
            }
            return true;
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Tạo mã QR Code dạng ảnh PNG từ chuỗi URL được chỉ định và lưu xuống ổ đĩa
    /// </summary>
    private async Task GenerateAndSaveQr(string url, string savePath)
    {
        using var qrGenerator = new QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        var qrBytes = qrCode.GetGraphic(20);
        await System.IO.File.WriteAllBytesAsync(savePath, qrBytes);
    }

    /// <summary>
    /// TÁC VỤ CHẠY NGẦM HANGFIRE: Dọn dẹp và cắt lại file 25% và 50% cho một tài liệu cụ thể
    /// </summary>
    public async Task ReRenderDocumentFilesAsync(int documentId)
    {
        var document = await _docRepo.FindByIdAsync(documentId);
        if (document == null || string.IsNullOrEmpty(document.FileUrl)) return;

        var webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var relativeFilePath = document.FileUrl.TrimStart('/');
        var filePath = Path.Combine(webRootPath, relativeFilePath.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (!System.IO.File.Exists(filePath)) return;

        var folder = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);

        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName)) return;

        var target25Path = Path.Combine(folder, "25_" + fileName);
        var target50Path = Path.Combine(folder, "50_" + fileName);

        try
        {
            // Dọn dẹp: Xóa bỏ các tệp tin lỗi thời cũ nếu đang tồn tại trước đó
            if (System.IO.File.Exists(target25Path)) System.IO.File.Delete(target25Path);
            if (System.IO.File.Exists(target50Path)) System.IO.File.Delete(target50Path);

            // Thực hiện tính toán cắt tệp tin mới
            GeneratePercentPdfsCombined(filePath, target25Path, target50Path);
        }
        catch (Exception)
        {
            // Ghi nhận log lỗi tại đây nếu ổ đĩa bị khóa hoặc lỗi I/O quyền truy cập
        }
    }
    #endregion

    /// <summary>
    /// Tiếp nhận yêu cầu tái xử lý file (Re-render) PDF 25% và 50%.
    /// Hỗ trợ 2 chế độ: Chạy đơn lẻ theo ID cụ thể hoặc quét phân trang hàng loạt toàn bộ database đẩy vào Hangfire Queue.
    /// </summary>
    [HttpGet("rerender-all")]
    public async Task<IActionResult> ReRenderAll(int? id, [FromServices] IBackgroundJobClient backgroundJobClient)
    {
        int totalJobsQueued = 0;

        // CHẾ ĐỘ 1: Xử lý đích danh một tài liệu đơn lẻ theo ID
        if (id.HasValue && id.Value > 0)
        {
            var document = await _docRepo.FindByIdAsync(id.Value);

            if (document != null && !string.IsNullOrEmpty(document.FileUrl))
            {
                backgroundJobClient.Enqueue<DocumentController>(x => x.ReRenderDocumentFilesAsync(document.Id));
                totalJobsQueued++;
                TempData["Success"] = $"Đã đẩy tài liệu ID #{document.Id} ('{document.Title}') vào Hangfire để làm mới file 25% và 50%.";
            }
            else
            {
                TempData["Error"] = $"Không tìm thấy tài liệu có ID #{id.Value} hoặc tài liệu này chưa có file gốc.";
            }

            return RedirectToAction("Index");
        }

        // CHẾ ĐỘ 2: Quét toàn bộ hệ thống theo kỹ thuật phân trang (Tránh quá tải bộ nhớ RAM)
        int pageSize = 100;
        int page = 1;
        bool hasData = true;

        while (hasData)
        {
            var (documents, totalItems) = await _docRepo.GetAdminPagedDocumentsAsync(
                search: null, isFree: null, categoryId: null, publisherId: null, authorIds: null, sortBy: null, page: page, pageSize: pageSize);

            var currentBatchIds = documents.Where(d => !string.IsNullOrEmpty(d.FileUrl)).Select(d => d.Id).ToList();

            if (currentBatchIds.Any())
            {
                foreach (var docId in currentBatchIds)
                {
                    backgroundJobClient.Enqueue<DocumentController>(x => x.ReRenderDocumentFilesAsync(docId));
                    totalJobsQueued++;
                }
                page++;
            }
            else
            {
                hasData = false;
            }

            if (page * pageSize > totalItems && !currentBatchIds.Any()) hasData = false;
        }

        if (totalJobsQueued == 0)
        {
            // Trả về thông báo lỗi nếu không quét được bất kỳ tệp tin nào hợp lệ
            TempData["Error"] = "Không tìm thấy tài liệu hợp lệ nào có file gốc để render lại.";
            return RedirectToAction("Index");
        }

        TempData["Success"] = $"Đã đẩy thành công {totalJobsQueued} tài liệu vào Hangfire để chạy ngầm tiến trình làm mới file 25% và 50%.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Hiển thị danh sách tài liệu khu vực Admin kết hợp đa bộ lọc dữ liệu và phân trang
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, bool? isFree, int? categoryId, int? publisherId, [FromQuery(Name = "authorIds")] List<int>? authorIds, string? sortBy, int page = 1)
    {
        int pageSize = 10;

        var (documents, totalItems) = await _docRepo.GetAdminPagedDocumentsAsync(
            search, isFree, categoryId, publisherId, authorIds, sortBy, page, pageSize);

        // Gửi trả dữ liệu bộ lọc phục vụ việc binding giữ lại trạng thái trên giao diện
        ViewBag.Search = search;
        ViewBag.IsFree = isFree;
        ViewBag.CategoryId = categoryId;
        ViewBag.PublisherId = publisherId;
        ViewBag.AuthorIds = authorIds ?? new List<int>();
        ViewBag.SortBy = sortBy;
        ViewBag.CurrentPage = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalItems = totalItems;

        // Load danh mục nguồn cho các thẻ Dropdown/Select HTML
        ViewBag.Categories = new SelectList(await _docRepo.GetRawCategoriesAsync(), "Id", "Name");
        ViewBag.Publishers = new SelectList(await _docRepo.GetRawPublishersAsync(), "Id", "Name");
        ViewBag.Authors = new MultiSelectList(await _docRepo.GetRawAuthorsAsync(), "Id", "Name");

        return View("~/Views/Admin/Document/Index.cshtml", documents);
    }

    /// <summary>
    /// Hiển thị giao diện Form thêm mới tài liệu
    /// </summary>
    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        ViewBag.Categories = new SelectList(await _docRepo.GetRawCategoriesAsync(), "Id", "Name");
        ViewBag.Publishers = new SelectList(await _docRepo.GetRawPublishersAsync(), "Id", "Name");
        ViewBag.Authors = new MultiSelectList(await _docRepo.GetRawAuthorsAsync(), "Id", "Name");
        return View("~/Views/Admin/Document/Create.cshtml");
    }

    /// <summary>
    /// Xử lý logic tạo mới tài liệu, lưu trữ file vật lý, tự động phân tách file và sinh mã QR Code đồng bộ
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create(string title, string description, int categoryId, int publisherId, List<int> authorIds, IFormFile file, IFormFile? thumb, DateTime publicDate, DateTime? reprintDate = null, bool isFree = true, decimal price = 0)
    {
        // Kiểm tra điều kiện tiên quyết bắt buộc của dữ liệu đầu vào
        if (string.IsNullOrWhiteSpace(title) || categoryId == 0 || publisherId == 0 || file == null || authorIds == null || !authorIds.Any())
        {
            TempData["Error"] = "Vui lòng nhập đầy đủ thông tin.";
            return RedirectToAction("Create");
        }

        if (reprintDate.HasValue && reprintDate < publicDate)
        {
            TempData["Error"] = "Năm tái bản không được nhỏ hơn năm xuất bản.";
            return RedirectToAction("Create");
        }

        var document = new Document
        {
            Title = title,
            Description = description,
            CategoryId = categoryId,
            PublisherId = publisherId,
            IsFree = isFree,
            Price = price,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            PublicDate = publicDate,
            ReprintDate = reprintDate,
        };

        await _docRepo.CreateAsync(document);
        await _docRepo.SaveAsync(); // Lưu trước để lấy ID thực tế của bản ghi cấp thư mục độc lập

        var documentFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/documents", $"document-{document.Id}");
        if (!Directory.Exists(documentFolder)) Directory.CreateDirectory(documentFolder);

        // Lưu trữ File PDF gốc lên ổ cứng
        var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
        var filePath = Path.Combine(documentFolder, fileName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        document.FileUrl = $"/documents/document-{document.Id}/{fileName}";

        // Tự động trích xuất file Demo Xem trước công khai (3 trang)
        var previewName = "preview_" + fileName;
        var previewPath = Path.Combine(documentFolder, previewName);
        if (GeneratePreviewPdf(filePath, previewPath))
        {
            document.PreviewFileUrl = $"/documents/document-{document.Id}/{previewName}";
        }

        // Tự động phân tách các tệp tin phục vụ đọc thử giới hạn (25% và 50%)
        var file25Path = Path.Combine(documentFolder, "25_" + fileName);
        var file50Path = Path.Combine(documentFolder, "50_" + fileName);
        GeneratePercentPdfsCombined(filePath, file25Path, file50Path);

        // Xử lý ảnh đại diện hiển thị (Ưu tiên ảnh upload, tự động render từ trang 1 PDF nếu trống)
        if (thumb != null)
        {
            var thumbFileName = "thumb_" + Guid.NewGuid() + Path.GetExtension(thumb.FileName);
            var thumbPath = Path.Combine(documentFolder, thumbFileName);
            using (var stream = new FileStream(thumbPath, FileMode.Create))
            {
                await thumb.CopyToAsync(stream);
            }
            document.Thumb = $"/documents/document-{document.Id}/{thumbFileName}";
        }
        else
        {
            var thumbFileName = "thumb.jpg";
            var thumbPath = Path.Combine(documentFolder, thumbFileName);
            await RenderPdfThumbnail(filePath, thumbPath);
            document.Thumb = $"/documents/document-{document.Id}/{thumbFileName}";
        }

        // Khởi tạo mã QR Code điều hướng đến trang chi tiết (View QR)
        string viewUrl = $"{Request.Scheme}://{Request.Host}/document/qr-detail/{document.Id}";
        string viewPath = Path.Combine(documentFolder, QRCodeFileName.View);
        await GenerateAndSaveQr(viewUrl, viewPath);
        await _docRepo.AddQrCodeAsync(new QRCode
        {
            DocumentId = document.Id,
            Type = QRCodeType.view,
            QrUrl = $"/documents/document-{document.Id}/{QRCodeFileName.View}"
        });

        // Khởi tạo mã QR Code điều hướng tải xuống trực tiếp (Download QR)
        string downloadUrl = $"{Request.Scheme}://{Request.Host}/document/qr-download/{document.Id}";
        string downloadPath = Path.Combine(documentFolder, QRCodeFileName.Download);
        await GenerateAndSaveQr(downloadUrl, downloadPath);
        await _docRepo.AddQrCodeAsync(new QRCode
        {
            DocumentId = document.Id,
            Type = QRCodeType.download,
            QrUrl = $"/documents/document-{document.Id}/{QRCodeFileName.Download}"
        });

        // Thiết lập mối quan hệ nhiều-nhiều (N-N) đối với danh sách Tác giả
        foreach (var authorId in authorIds)
        {
            _docRepo.AddDocumentAuthor(new DocumentAuthor { DocumentId = document.Id, AuthorId = authorId });
        }

        // Lưu cập nhật tối hậu toàn bộ đường dẫn tệp tin liên quan vào cơ sở dữ liệu
        _docRepo.Update(document);
        await _docRepo.SaveAsync();

        TempData["Success"] = "Tạo tài liệu thành công.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Hiển thị thông tin chi tiết tài liệu kèm danh sách liên kết tác giả hiện tại để chỉnh sửa
    /// </summary>
    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(int id)
    {
        var doc = await _docRepo.FindWithAuthorsByIdAsync(id);
        if (doc == null) return NotFound();

        ViewBag.Categories = new SelectList(await _docRepo.GetRawCategoriesAsync(), "Id", "Name", doc.CategoryId);
        ViewBag.Publishers = new SelectList(await _docRepo.GetRawPublishersAsync(), "Id", "Name", doc.PublisherId);
        ViewBag.Authors = new MultiSelectList(await _docRepo.GetRawAuthorsAsync(), "Id", "Name", doc.DocumentAuthors.Select(da => da.AuthorId));

        return View("~/Views/Admin/Document/Edit.cshtml", doc);
    }

    /// <summary>
    /// Cập nhật thông tin thay đổi. Nếu có file mới đi kèm, hệ thống dọn dẹp sạch sẽ kho tệp tin cũ để giải phóng ổ cứng.
    /// </summary>
    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(int id, string title, string description, int categoryId, int publisherId, List<int> authorIds, IFormFile? file, DateTime publicDate, DateTime? reprintDate = null, bool isFree = true, decimal price = 0)
    {
        var document = await _docRepo.FindWithAuthorsByIdAsync(id);
        if (document == null) return NotFound();

        if (reprintDate.HasValue && reprintDate < publicDate)
        {
            TempData["Error"] = "Năm tái bản không được nhỏ hơn năm xuất bản.";
            return RedirectToAction("Edit", new { id });
        }

        document.Title = title;
        document.Description = description;
        document.CategoryId = categoryId;
        document.PublisherId = publisherId;
        document.PublicDate = publicDate;
        document.ReprintDate = reprintDate;
        document.IsFree = isFree;
        document.Price = price;

        var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/documents", $"document-{document.Id}");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        if (file != null)
        {
            // BƯỚC 1: Dọn dẹp cục bộ dữ liệu tệp tin vật lý cũ trên hệ thống tránh lãng phí dung lượng
            if (!string.IsNullOrEmpty(document.FileUrl))
            {
                var oldFileName = Path.GetFileName(document.FileUrl);
                var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", document.FileUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(oldFilePath)) System.IO.File.Delete(oldFilePath);

                var old25Path = Path.Combine(folder, "25_" + oldFileName);
                var old50Path = Path.Combine(folder, "50_" + oldFileName);
                if (System.IO.File.Exists(old25Path)) System.IO.File.Delete(old25Path);
                if (System.IO.File.Exists(old50Path)) System.IO.File.Delete(old50Path);
            }
            if (!string.IsNullOrEmpty(document.PreviewFileUrl))
            {
                var oldPreviewPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", document.PreviewFileUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(oldPreviewPath)) System.IO.File.Delete(oldPreviewPath);
            }
            if (!string.IsNullOrEmpty(document.Thumb) && document.Thumb.Contains("thumb.jpg"))
            {
                var oldThumbPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", document.Thumb.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(oldThumbPath)) System.IO.File.Delete(oldThumbPath);
            }

            // BƯỚC 2: Ghi dữ liệu file PDF mới thay thế và chạy lại các tiến trình render đồng bộ
            var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(folder, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            document.FileUrl = $"/documents/document-{document.Id}/{fileName}";

            var previewName = "preview_" + fileName;
            var previewPath = Path.Combine(folder, previewName);
            if (GeneratePreviewPdf(filePath, previewPath))
            {
                document.PreviewFileUrl = $"/documents/document-{document.Id}/{previewName}";
            }

            var file25Path = Path.Combine(folder, "25_" + fileName);
            var file50Path = Path.Combine(folder, "50_" + fileName);
            GeneratePercentPdfsCombined(filePath, file25Path, file50Path);

            var thumbPath = Path.Combine(folder, "thumb.jpg");
            await RenderPdfThumbnail(filePath, thumbPath);
            document.Thumb = $"/documents/document-{document.Id}/thumb.jpg";
        }
        else if (string.IsNullOrEmpty(document.PreviewFileUrl) && !string.IsNullOrEmpty(document.FileUrl))
        {
            // Nhánh xử lý bổ sung: Tự sinh file Preview nếu phát hiện tài liệu cũ bị thiếu file Preview công khai
            var sourcePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", document.FileUrl.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (System.IO.File.Exists(sourcePath))
            {
                var previewName = "preview_" + Path.GetFileName(document.FileUrl);
                var previewPath = Path.Combine(folder, previewName);
                if (GeneratePreviewPdf(sourcePath, previewPath))
                {
                    document.PreviewFileUrl = $"/documents/document-{document.Id}/{previewName}";
                }
            }
        }

        // BƯỚC 3: Làm mới, xóa sạch thực thể ảnh QR Code cũ và cấu hình lại từ đầu
        var viewQrPath = Path.Combine(folder, QRCodeFileName.View);
        var downloadQrPath = Path.Combine(folder, QRCodeFileName.Download);
        if (System.IO.File.Exists(viewQrPath)) System.IO.File.Delete(viewQrPath);
        if (System.IO.File.Exists(downloadQrPath)) System.IO.File.Delete(downloadQrPath);

        await _docRepo.DeleteQrsByDocIdAsync(document.Id);

        string viewUrl = $"{Request.Scheme}://{Request.Host}/document/qr-detail/{document.Id}";
        string viewQrUrl = $"/documents/document-{document.Id}/{QRCodeFileName.View}";
        await GenerateAndSaveQr(viewUrl, viewQrPath);
        await _docRepo.AddQrCodeAsync(new QRCode { DocumentId = document.Id, Type = QRCodeType.view, QrUrl = viewQrUrl });

        string downloadUrl = $"{Request.Scheme}://{Request.Host}/document/qr-download/{document.Id}";
        string downloadQrUrl = $"/documents/document-{document.Id}/{QRCodeFileName.Download}";
        await GenerateAndSaveQr(downloadUrl, downloadQrPath);
        await _docRepo.AddQrCodeAsync(new QRCode { DocumentId = document.Id, Type = QRCodeType.download, QrUrl = downloadQrUrl });

        // BƯỚC 4: Kiểm tra và tối ưu cập nhật danh sách tác giả (So sánh thay đổi thông minh)
        var currentAuthorIds = document.DocumentAuthors.Select(da => da.AuthorId).ToList();
        bool isAuthorsChanged = authorIds.Count != currentAuthorIds.Count || !authorIds.All(currentAuthorIds.Contains);

        if (isAuthorsChanged)
        {
            _docRepo.RemoveDocumentAuthors(document.DocumentAuthors);
            await _docRepo.SaveAsync(); // Đồng bộ dọn dẹp sạch quan hệ cũ trước khi add mới

            foreach (var authorId in authorIds)
            {
                _docRepo.AddDocumentAuthor(new DocumentAuthor { DocumentId = document.Id, AuthorId = authorId });
            }
        }

        _docRepo.Update(document);
        await _docRepo.SaveAsync();

        TempData["Success"] = "Cập nhật tài liệu thành công.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Xóa hoàn toàn tài liệu, quét sạch thư mục lưu trữ file trên ổ đĩa vật lý và gỡ các ràng buộc liên quan trong DB
    /// </summary>
    [HttpGet("delete/{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await _docRepo.FindWithAuthorsByIdAsync(id);
        if (doc == null)
        {
            TempData["Error"] = "Không tìm thấy tài liệu cần xóa.";
            return RedirectToAction("Index");
        }

        try
        {
            // Thực hiện quét xóa toàn bộ thư mục chứa tệp tin vật lý bao gồm cả các nhánh con (recursive)
            var docFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/documents", $"document-{doc.Id}");
            if (Directory.Exists(docFolder))
            {
                Directory.Delete(docFolder, recursive: true);
            }

            // Gỡ bỏ liên kết khóa ngoại quan hệ tác giả
            if (doc.DocumentAuthors != null && doc.DocumentAuthors.Any())
            {
                _docRepo.RemoveDocumentAuthors(doc.DocumentAuthors);
            }

            // Gỡ bỏ danh sách QR Code liên kết và thực hiện xóa bản ghi tài liệu chính gốc
            await _docRepo.DeleteQrsByDocIdAsync(doc.Id);
            _docRepo.Delete(doc);
            await _docRepo.SaveAsync();

            TempData["Success"] = "Xóa tài liệu thành công.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Có lỗi xảy ra khi xóa tài liệu: " + ex.Message;
        }

        return RedirectToAction("Index");
    }
}
