using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using WowzaSample.Hubs;
using WowzaSample.Models;
using WowzaSample.Components;

var builder = WebApplication.CreateBuilder(args);

// Cross-origin policy — restricted to localhost origins for development.
// For production, replace with your actual domain(s).
builder.Services.AddCors(o => o.AddPolicy("CorsPolicy", b =>
{
    b.WithOrigins(
        "https://localhost:5001",
        "http://localhost:5000",
        "https://localhost:44393",
        "http://localhost:17721"
    )
     .AllowAnyMethod()
     .AllowAnyHeader()
     .AllowCredentials();
}));

builder.Services.AddSignalR();

// Blazor Web App with interactive server + WebAssembly components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// In-memory singleton state for active calls
builder.Services.AddSingleton<List<User>>();
builder.Services.AddSingleton<List<UserCall>>();
builder.Services.AddSingleton<List<CallOffer>>();

// Toast notification service (scoped per circuit)
builder.Services.AddScoped<WowzaSample.Components.Dialogs.ToastService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseCors("CorsPolicy");
app.UseAntiforgery();

app.MapHub<WebRTCHub>("/Hubs/WebRTCHub");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

app.Run();
