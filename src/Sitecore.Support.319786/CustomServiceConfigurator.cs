namespace Sitecore.Support.EmailCampaign.Server.DependencyInjection
{
  using System.Reflection;
  using Microsoft.Extensions.DependencyInjection;
  using Sitecore.DependencyInjection;
  using Sitecore.Services.Infrastructure.Sitecore.DependencyInjection;

  public class CustomServiceConfigurator : IServicesConfigurator
  {
    public void Configure(IServiceCollection serviceCollection)
    {
      Assembly[] assemblies = new Assembly[1]
      {
          typeof(CustomServiceConfigurator).Assembly
      };

      serviceCollection.AddWebApiControllers(assemblies);
    }
  }
}