using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using thuvienso.Models;

namespace thuvienso.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // ĐĂNG KÝ CÁC THÀNH PHẦN KHAI BÁO THỰC THỂ (DBSET)
        public DbSet<User> Users { get; set; }
        public DbSet<Author> Authors { get; set; }
        public DbSet<Publisher> Publishers { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<DocumentAuthor> DocumentAuthors { get; set; }
        public DbSet<Download> Downloads { get; set; }
        public DbSet<QRCode> QRCodes { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================================================================
            // 1. CHUẨN HÓA ENUMS: TỰ ĐỘNG CHUYỂN ĐỔI SANG CHUỖI KHI LƯU TRỮ VÀO MYSQL
            // =========================================================================

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(u => u.Role)
                    .HasConversion<string>()
                    .HasColumnType("varchar(20)");
                    //.HasDefaultValue(UserRole.user);
            });

            modelBuilder.Entity<OrderDetail>(entity =>
            {
                entity.Property(od => od.Status)
                    .HasConversion<string>()
                    .HasColumnType("varchar(20)")
                    .HasDefaultValue(OrderStatus.pending);

                entity.Property(od => od.CanceledBy)
                    .HasConversion<string>()
                    .HasColumnType("varchar(20)");
            });

            modelBuilder.Entity<QRCode>(entity =>
            {
                entity.Property(q => q.Type)
                    .HasConversion<string>()
                    .HasColumnType("varchar(20)");
            });

            // =========================================================================
            // 2. RÀNG BUỘC CHỈ MỤC VÀ TÍNH DUY NHẤT (INDEX & UNIQUE CONSTRAINTS)
            // =========================================================================
            modelBuilder.Entity<OrderDetail>().HasIndex(od => od.OrderCode).IsUnique();

            // =========================================================================
            // 3. CẤU HÌNH QUAN HỆ & ĐƯỜNG DẪN XÓA DỮ LIỆU (CASCADE PATHS)
            // =========================================================================
            modelBuilder.Entity<Category>()
                .HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Document>()
                .HasOne(d => d.Category)
                .WithMany(c => c.Documents)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Document>()
                .HasOne(d => d.Publisher)
                .WithMany(p => p.Documents)
                .HasForeignKey(d => d.PublisherId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Order)
                .WithMany(o => o.OrderDetails)
                .HasForeignKey(od => od.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.CanceledByUser)
                .WithMany()
                .HasForeignKey(od => od.CanceledById)
                .OnDelete(DeleteBehavior.Restrict);

            // =========================================================================
            // 🌟 4. CẤU HÌNH ĐỔI MÚI GIỜ TOÀN CỤC KHI ĐỌC DATE (UTC -> HO CHI MINH)
            // =========================================================================

            // Xác định Id múi giờ tương thích đa nền tảng (Windows dùng "SE Asia Standard Time", Linux/Docker dùng "Asia/Ho_Chi_Minh")
            string timeZoneId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "SE Asia Standard Time"
                : "Asia/Ho_Chi_Minh";

            var targetTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

            // Bộ chuyển đổi cho kiểu dữ liệu DateTime thông thường
            var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
                v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(), // Khi ghi vào DB: Ép về UTC
                v => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(v, DateTimeKind.Utc), targetTimeZone) // Khi đọc ra: UTC -> GMT+7
            );

            // Bộ chuyển đổi cho kiểu dữ liệu DateTime? (Nullable) đề phòng các trường hợp ngày có thể null
            var nullableDateTimeConverter = new ValueConverter<DateTime?, DateTime?>(
                v => !v.HasValue ? v : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()), // Khi ghi vào DB
                v => !v.HasValue ? v : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc), targetTimeZone) // Khi đọc ra
            );

            // Duyệt qua tất cả các bảng và cấu hình tự động bộ converter cho các cột ngày tháng
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.GetProperties();

                foreach (var property in properties)
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        property.SetValueConverter(dateTimeConverter);
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        property.SetValueConverter(nullableDateTimeConverter);
                    }
                }
            }
        }

        // =========================================================================
        // 5. BỘ ĐIỀU PHỐI THỜI GIAN TOÀN CỤC (TỰ ĐỘNG ĐIỀN UTC_NOW CHO MỌI MODEL)
        // =========================================================================
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow; // Luôn luôn lưu giờ UTC chuẩn hóa xuống MySQL

            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                var createdProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CreatedAt");
                if (createdProp != null && entry.State == EntityState.Added)
                {
                    createdProp.CurrentValue = now;
                }

                var updatedProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "UpdatedAt");
                if (updatedProp != null)
                {
                    updatedProp.CurrentValue = now;
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
