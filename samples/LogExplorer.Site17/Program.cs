// This site's database belongs to Umbraco 17; refuse to boot on another major so it is never
// upgraded (or downgraded) by accident. Build and run it with `-p:UmbracoMajor=17` (ADR 0010).
int umbracoMajor = typeof(Umbraco.Cms.Core.Constants).Assembly.GetName().Version!.Major;
if (umbracoMajor != 17)
{
    throw new InvalidOperationException(
        $"LogExplorer.Site17 needs Umbraco 17 but was built against Umbraco {umbracoMajor}. "
            + "Run it with: dotnet run --project samples/LogExplorer.Site17 -p:UmbracoMajor=17"
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
