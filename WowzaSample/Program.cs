using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using WowzaSample.Hubs;
using WowzaSample.Models;

var builder = WebApplication.CreateBuilder(args);

// Cross-origin policy to accept request from any origin (e.g. localhost:8084).
// Note: SetIsOriginAllowed is used instead of AllowAnyOrigin to remain
// compatible with AllowCredentials (AllowAnyOrigin + AllowCredentials throws
// InvalidOperationException in ASP.NET Core 3.0+).
builder.Services.AddCors(o => o.AddPolicy("CorsPolicy", b =>
{
    b.AllowAnyMethod()
     .AllowAnyHeader()
     .SetIsOriginAllowed(_ => true)
     .AllowCredentials();
}));

builder.Services.AddSignalR();
builder.Services.AddControllersWithViews();

// In-memory singleton state for active calls
builder.Services.AddSingleton<List<User>>();
builder.Services.AddSingleton<List<UserCall>>();
builder.Services.AddSingleton<List<CallOffer>>();

var app = builder.Build();

app.UseStaticFiles();
app.UseFileServer();
app.UseCors("CorsPolicy");

app.MapControllers();
app.MapHub<WebRTCHub>("/Hubs/WebRTCHub");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
