namespace Sitecore.Support.EmailCampaign.Server.Filters
{
  using Sitecore.Diagnostics;
  using Sitecore.Security.Accounts;
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.Web.Http;
  using System.Web.Http.Controllers;

  [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "Reviewed. Suppression is OK here.")]
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
  public sealed class SitecoreAuthorizeAttribute : AuthorizeAttribute
  {
    private static readonly SitecoreAuthorizeAttribute.ITicketManager TicketManager = (SitecoreAuthorizeAttribute.ITicketManager)new SitecoreAuthorizeAttribute.TicketManagerWrapper();

    public SitecoreAuthorizeAttribute(params string[] roles)
    {
      this.Roles = string.Join(",", roles);
    }

    public bool AdminsOnly { get; set; }

    protected override bool IsAuthorized(HttpActionContext actionContext)
    {
      Assert.ArgumentNotNull((object)actionContext, nameof(actionContext));
      int num1 = !base.IsAuthorized(actionContext) ? 0 : (!this.AdminsOnly ? 1 : 0);
      User principal = actionContext.ControllerContext.RequestContext.Principal as User;
      int num2 = !((Account)principal != (Account)null) ? (false ? 1 : 0) : (principal.IsAdministrator ? 1 : 0);
      if ((num1 | num2) != 0)
        return SitecoreAuthorizeAttribute.TicketManager.IsCurrentTicketValid();
      return false;
    }

    internal interface ITicketManager
    {
      bool IsCurrentTicketValid();
    }

    private class TicketManagerWrapper : SitecoreAuthorizeAttribute.ITicketManager
    {
      public bool IsCurrentTicketValid()
      {
        return Sitecore.Web.Authentication.TicketManager.IsCurrentTicketValid();
      }
    }
  }
}