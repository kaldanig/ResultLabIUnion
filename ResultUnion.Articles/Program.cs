using ResultUnion.Articles;

// The getting-started endpoint as a running app; ResultUnion.Articles.Tests calls it through WebApplicationFactory to
// check the problem response the article shows.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<IUserRepository>(new InMemoryUsers());
builder.Services.AddScoped<UserService>();

var app = builder.Build();
GettingStartedSnippets.Map(app);
app.Run();

public partial class Program;
