using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace LogExplorer.Samples.LogGenerator;

/// <summary>
/// Registers the sample-site log generator. It is compiled into each sample site (not the package),
/// and does nothing unless enabled in the <c>LogGenerator</c> configuration section.
/// </summary>
public sealed class LogGeneratorComposer : IComposer
{
    /// <summary>Binds <see cref="LogGeneratorOptions"/> and adds the background service.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<LogGeneratorOptions>(
            builder.Config.GetSection(LogGeneratorOptions.SectionName)
        );
        builder.Services.AddHostedService<LogGeneratorService>();
    }
}
