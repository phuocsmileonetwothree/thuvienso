using Microsoft.EntityFrameworkCore;
using thuvienso.Data;
using thuvienso.Hubs;
using thuvienso.Middlewares;
using thuvienso.Repositories;
using thuvienso.Services;
using Hangfire;
using Hangfire.MySql;
using System.Transactions;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Ghi nhận tiến trình khởi chạy hệ thống
    File.WriteAllText("logs/checkpoint1.txt", "Application started successfully.");

    // Đăng ký dịch vụ MVC và cấu hình Global Filters
    builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add<KeepFilterAfterSubmit>();
    });

    // CẤU HÌNH NHẬN DIỆN FORWARDED HEADERS (REVERSE PROXY / DEV TUNNEL)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost |
                                   Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;

        // Xóa cấu hình Networks/Proxies mặc định để chấp nhận các Request Header từ Visual Studio Dev Tunnels
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Cấu hình Cookie Authentication cho phân hệ Quản trị (Admin)
    builder.Services.AddAuthentication("AdminAuth")
        .AddCookie("AdminAuth", options =>
        {
            options.LoginPath = "/admin/auth/login";
            options.LogoutPath = "/admin/auth/logout";
            options.AccessDeniedPath = "/admin/auth/denied";
        });

    builder.Services.AddAuthorization();

    // CẤU HÌNH KẾT NỐI CƠ SỞ DỮ LIỆU MYSQL VÀ LAZY LOADING PROXIES
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "NULL";
    File.WriteAllText("logs/connection.txt", connectionString);

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseLazyLoadingProxies()
               .UseMySql(
                    connectionString,
                    ServerVersion.AutoDetect(connectionString)
                )
    );

    // =========================================================================
    // CẤU HÌNH HANGFIRE BACKGROUND JOB VỚI MYSQL STORAGE
    // =========================================================================
    builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
        {
            // Thiết lập mức cô lập IsolationLevel tối ưu để tránh lỗi Deadlock khi quét và cập nhật hàng đợi
            TransactionIsolationLevel = IsolationLevel.ReadCommitted,
            QueuePollInterval = TimeSpan.FromSeconds(15),         // Tần suất kiểm tra tác vụ mới trong hàng đợi
            JobExpirationCheckInterval = TimeSpan.FromHours(1),   // Tần suất quét và dọn dẹp lịch sử tác vụ hết hạn
            CountersAggregateInterval = TimeSpan.FromMinutes(5),  // Tần suất tổng hợp dữ liệu bộ đếm thống kê
            PrepareSchemaIfNecessary = true,                      // Tự động khởi tạo cấu trúc bảng chức năng nếu chưa có
            DashboardJobListLimit = 50000,
            TablesPrefix = "hf_"                                  // Tiền tố gom nhóm các bảng thuộc phân hệ Hangfire
        })));

    // Kích hoạt tiến trình xử lý tác vụ ngầm (Background Job Server Worker)
    builder.Services.AddHangfireServer();
    // =========================================================================

    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(30);
    });

    // Đăng ký các thành phần thuộc tầng Repositories (Dependency Injection)
    builder.Services.AddScoped<DocumentRepository>();
    builder.Services.AddScoped<OrderRepository>();

    // Đăng ký các thành phần thuộc tầng Services (Dependency Injection)
    builder.Services.AddHttpClient();
    builder.Services.AddScoped<PaymentService>();
    builder.Services.AddScoped<thuvienso.Interfaces.IPaymentGateway, thuvienso.Services.PaymentGateway.PayOSService>();
    builder.Services.AddScoped<OrderService>();
    builder.Services.AddScoped<MailService>();
    builder.Services.AddScoped<DocumentService>();
    builder.Services.AddSignalR();

    var app = builder.Build();

    app.UseForwardedHeaders();

    // THIẾT LẬP HTTP REQUEST PIPELINE (MIDDLEWARE)
    app.UseStaticFiles();
    app.UseRouting();

    // Kích hoạt bảng điều khiển quản trị tác vụ ngầm Hangfire Dashboard
    app.UseHangfireDashboard("/hangfire");

    app.UseSession();
    app.UseAuthentication();
    app.UseAuthorization();

    // Middleware kiểm soát quyền truy cập và điều hướng phân hệ Quản trị (Admin Area)
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isAdminArea = path.StartsWithSegments("/admin");

        if (isAdminArea && path == "/admin")
        {
            context.Response.Redirect("/admin/dashboard");
            return;
        }

        var isLoggedIn = context.User?.Identity?.IsAuthenticated ?? false;
        if (isAdminArea && !isLoggedIn &&
            !path.StartsWithSegments("/admin/auth") &&
            context.Request.Method == "GET")
        {
            context.Response.Redirect("/admin/auth/login");
            return;
        }

        await next();
    });

    // Middleware kiểm soát quyền truy cập và điều hướng phân hệ Người dùng (End-User Protected Routes)
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isUserProtected = path.StartsWithSegments("/user/profile")
                            || path.StartsWithSegments("/user/payment")
                            || path.StartsWithSegments("/document");

        var userId = context.Session.GetInt32("UserId");
        if (isUserProtected && userId == null && context.Request.Method == "GET")
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            context.Session.SetString("ReturnUrl", returnUrl);
            context.Response.Redirect($"/user/auth/login");
            return;
        }
        else if (userId != null && context.Session.Keys.Contains("ReturnUrl"))
        {
            var returnUrl = context.Session.GetString("ReturnUrl");
            context.Session.Remove("ReturnUrl");
            context.Response.Redirect(returnUrl ?? "/");
            return;
        }

        await next();
    });

    // CẤU HÌNH ENDPOINTS (SIGNALR HUB & ROUTING)
    app.MapHub<PaymentHub>("/paymentHub");

    app.MapControllerRoute(
        name: "user",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    app.Run();
}
catch (Exception ex)
{
    // Ghi nhận lỗi nghiêm trọng xảy ra trong quá trình khởi chạy ứng dụng
    File.WriteAllText("logs/startup-error.txt", ex.ToString());
    throw;
}
