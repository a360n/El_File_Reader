using EcoLabReaderApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register application services
builder.Services.AddSingleton<FileRestructurerService>();
builder.Services.AddSingleton<ElParserService>();
builder.Services.AddSingleton<AuditStorageService>();
builder.Services.AddSingleton<TiffImageService>();
builder.Services.AddSingleton<PdfExportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Audit/Error");
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Audit}/{action=Index}/{id?}");

// Execute initial restructuring on application startup
using (var scope = app.Services.CreateScope())
{
    var restructurer = scope.ServiceProvider.GetRequiredService<FileRestructurerService>();
    restructurer.RunRestructuring();
}

app.Run();
