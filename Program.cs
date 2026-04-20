using Microsoft.EntityFrameworkCore;
using FvCore.Data;
using FvCore.Services;
using Serilog;

var logPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "logs");
if (!Directory.Exists(logPath))
{
    Directory.CreateDirectory(logPath);
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(logPath, "log.txt"),
        rollingInterval: RollingInterval.Day,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1)
    )
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Services ──
builder.Services.AddControllersWithViews();

// Entity Framework Core with SQL Server
builder.Services.AddDbContext<FvCoreDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Http Client for API requests (Streaming Proxy)
builder.Services.AddHttpClient();

// Genre Classifier (singleton - stateless)
builder.Services.AddSingleton<GenreClassifierService>();

// Scanner Service (can be injected safely as singleton now to allow manual run)
builder.Services.AddSingleton<ScannerService>();

// Google Drive Auth Service
builder.Services.AddSingleton<GoogleDriveAuthService>();

var app = builder.Build();

// ── Startup DB Integrity ──
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<FvCoreDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var indexName = new Microsoft.Data.SqlClient.SqlParameter("@indexName", "IX_MediaItems_GoogleDriveId_Unique");
        var tableName = new Microsoft.Data.SqlClient.SqlParameter("@tableName", "MediaItems");
        var columnName = new Microsoft.Data.SqlClient.SqlParameter("@columnName", "ArtistName");

        context.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = @indexName) 
            BEGIN
                DECLARE @sqlIndex NVARCHAR(MAX) = N'CREATE UNIQUE NONCLUSTERED INDEX ' + QUOTENAME(@indexName) + 
                                                  N' ON [dbo].' + QUOTENAME(@tableName) + N' ([GoogleDriveId]) WHERE [GoogleDriveId] IS NOT NULL';
                EXEC sp_executesql @sqlIndex;
            END
            
            IF NOT EXISTS (
                SELECT * FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @tableName AND COLUMN_NAME = @columnName
            )
            BEGIN
                DECLARE @sqlAlter NVARCHAR(MAX) = N'ALTER TABLE [dbo].' + QUOTENAME(@tableName) + 
                                                  N' ADD ' + QUOTENAME(@columnName) + N' NVARCHAR(300) NULL';
                EXEC sp_executesql @sqlAlter;
            END
        ", indexName, tableName, columnName);
        logger.LogInformation("Database integrity checks passed/applied at startup using parameterized queries.");
    }
    catch (Exception ex)
    {
        logger.LogWarning("Failed to run startup DB integrity scripts (may be SQLite or missing rights): {Msg}", ex.Message);
    }
}

// ── Pipeline ──
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
