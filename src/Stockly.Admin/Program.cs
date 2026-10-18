using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Stockly.Admin;
using Stockly.Admin.Configuration;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddStocklyAdmin(AdminOptions.From(builder.Configuration[AdminOptions.ApiBaseUrlKey]));

await builder.Build().RunAsync();
