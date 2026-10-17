using DigitalSorter.Core;
using DigitalSorter.Data.GoogleDrive;
using DigitalSorter.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrokerClient>();
builder.Services.AddScoped<DriveOptions>();
builder.Services.AddScoped<IAccessTokenProvider>(s => s.GetRequiredService<BrokerClient>());
builder.Services.AddScoped<IMediaLibrarySource>(s => new GoogleDriveSource(new HttpClient(), s.GetRequiredService<IAccessTokenProvider>(), s.GetRequiredService<DriveOptions>()));
await builder.Build().RunAsync();
