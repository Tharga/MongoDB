using Blazored.LocalStorage;
using Radzen;
using Tharga.Blazor.Framework;
using Tharga.MongoDB;
using Tharga.MongoDB.Configuration;
using Tharga.TemplateBlazor.Web.Components;
using Tharga.TemplateBlazor.Web.Framework;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = Constants.ThemeStorageName;
    options.Duration = TimeSpan.FromDays(365);
});

builder.Services.AddThargaBlazor(o =>
{
    o.Title = "Tharga Template Site";
});

builder.AddMongoDB(o =>
{
    o.AssureIndex = AssureIndexMode.Disabled;
    //o.Monitor.StorageMode = MonitorStorageMode.Memory;
    //o.AssureIndex = AssureIndexMode.Disabled;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Tharga.TemplateBlazor.Web.Client._Imports).Assembly);

app.UseMongoDB();

app.Run();
