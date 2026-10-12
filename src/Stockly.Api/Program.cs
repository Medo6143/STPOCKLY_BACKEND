var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

await app.RunAsync();

/// <summary>Entry point; public so <c>WebApplicationFactory&lt;Program&gt;</c> can host the Api in tests.</summary>
public partial class Program;
