var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();
app.UseFileServer();
app.MapHub<GameHub>("/hub");
app.Run();