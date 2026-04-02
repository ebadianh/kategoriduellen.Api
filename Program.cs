using System.Text.Json.Serialization;
using kategoriduellen.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
  o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Map endpoints via extension
app.MapGameEndpoints();

app.Run();
