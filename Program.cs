using Microsoft.EntityFrameworkCore;
using FvCore.Data;
using FvCore.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──
builder.Services.AddControllersWithViews();

// Entity Framework Core with SQL Server
builder.Services.AddDbContext<FvCoreDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Genre Classifier (singleton - stateless)
builder.Services.AddSingleton<GenreClassifierService>();

// Scanner Service (runs on startup to index media files)
builder.Services.AddHostedService<ScannerService>();

var app = builder.Build();

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

Console.WriteLine(@"
╔══════════════════════════════════════════════════════╗
║                                                      ║
║   ███████╗██╗   ██╗     ██████╗ ██████╗ ██████╗ ███████╗  ║
║   ██╔════╝██║   ██║    ██╔════╝██╔═══██╗██╔══██╗██╔════╝  ║
║   █████╗  ██║   ██║    ██║     ██║   ██║██████╔╝█████╗    ║
║   ██╔══╝  ╚██╗ ██╔╝    ██║     ██║   ██║██╔══██╗██╔══╝    ║
║   ██║      ╚████╔╝     ╚██████╗╚██████╔╝██║  ██║███████╗  ║
║   ╚═╝       ╚═══╝       ╚═════╝ ╚═════╝ ╚═╝  ╚═╝╚══════╝  ║
║                                                      ║
║   Cyberpunk Digital Streaming Platform               ║
║   .NET 8 • Entity Framework Core • SQL Server        ║
║                                                      ║
╚══════════════════════════════════════════════════════╝
");

app.Run();
