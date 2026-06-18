using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using WowzaSample.Hubs;
using WowzaSample.Models;
using WowzaSample.Components;

var builder = WebApplication.CreateBuilder(args);

// Cross-origin policy to accept request from any origin.
builder.Services.AddCors(o => o.AddPolicy("CorsPolicy", b =>
{
    b.AllowAnyMethod()
     .AllowAnyHeader()
     .SetIsOriginAllowed(_ => true)
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

var app = builder.Build();

app.UseStaticFiles();
app.UseCors("CorsPolicy");
app.UseAntiforgery();

app.MapHub<WebRTCHub>("/Hubs/WebRTCHub");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();

app.Run();
