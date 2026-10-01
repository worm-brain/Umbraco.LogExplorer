// This site's database belongs to Umbraco 18; refuse to boot on another major so it is never
// upgraded (or downgraded) by accident. Build and run it with `-p:UmbracoMajor=18` (ADR 0010).
int umbracoMajor = typeof(Umbraco.Cms.Core.Constants).Assembly.GetName().Version!.Major;
if (umbracoMajor != 18)
{
    throw new InvalidOperationException(
        $"LogExplorer.Site18 needs Umbraco 18 but was built against Umbraco {umbracoMajor}. "
            + "Run it with: dotnet run --project samples/LogExplorer.Site18 -p:UmbracoMajor=18"
    );
}
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder().AddBackOffice().AddWebsite().AddComposers().Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
