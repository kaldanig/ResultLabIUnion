using ResultUnion.Articles;

// The getting-started endpoint as a running app; ResultUnion.Articles.Tests calls it through WebApplicationFactory to
// check the problem response the article shows, in English and, with Accept-Language: ka, in Georgian (localization.md).
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLocalization();
builder.Services.AddSingleton<ErrorMessages>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Translate(context.HttpContext.RequestServices.GetRequiredService<ErrorMessages>()));
builder.Services.AddSingleton<IUserRepository>(new InMemoryUsers());
builder.Services.AddScoped<UserService>();

var app = builder.Build();
app.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures("en", "ka")
    .AddSupportedUICultures("en", "ka"));
GettingStartedSnippets.Map(app);
await app.RunAsync();

public partial class Program;
