namespace Sitecore.Support.EmailCampaign.Server.Controllers.Dispatch
{
  using Sitecore.Configuration;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.EmailCampaign.Model.Web;
  using Sitecore.EmailCampaign.Server.Contexts;
  using Sitecore.EmailCampaign.Server.Controllers.Dispatch;
  using Sitecore.EmailCampaign.Server.Responses;
  using Sitecore.ExM.Framework.Diagnostics;
  using Sitecore.Modules.EmailCampaign.Application.EmailDispatch;
  using Sitecore.Modules.EmailCampaign.Core;
  using Sitecore.Modules.EmailCampaign.Core.Dispatch;
  using Sitecore.Modules.EmailCampaign.Messages;
  using Sitecore.Modules.EmailCampaign.Services;
  using Sitecore.Modules.EmailCampaign.Validators;
  using Sitecore.Services.Core;
  using Sitecore.Services.Infrastructure.Web.Http;
  using Sitecore.Support.EmailCampaign.Server.Filters;
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Web.Http;

  [ServicesController("EXM.SupportStartTest")]
  [SitecoreAuthorize(new string[] { "sitecore\\EXM Advanced Users", "sitecore\\EXM Users" })]
  public class StartTestController : ServicesApiController
  {
    public const string DateFormat = "yyyyMMddT000000";
    private readonly IExmCampaignService _exmCampaignService;
    private readonly IDispatchErrorHandler _dispatchErrorHandler;
    private readonly IAbnTestService _abnTestService;
    private readonly ITestDispatch _testDispatch;

    public StartTestController(IExmCampaignService exmCampaignService, ILogger logger, IAbnTestService abnTestService)
      : this(exmCampaignService, Sitecore.Modules.EmailCampaign.Application.Application.Instance.TestDispatch, (IDispatchErrorHandler)new DispatchErrorHandler(exmCampaignService, new ItemUtilExt(), logger, (RegexValidator)Factory.CreateObject("emailRegexValidator", true)), abnTestService)
    {
    }

    public StartTestController(IExmCampaignService exmCampaignService, ITestDispatch testDispatch, IDispatchErrorHandler dispatchErrorHandler, IAbnTestService abnTestService)
    {
      Assert.ArgumentNotNull((object)exmCampaignService, nameof(exmCampaignService));
      Assert.ArgumentNotNull((object)testDispatch, nameof(testDispatch));
      Assert.ArgumentNotNull((object)dispatchErrorHandler, nameof(dispatchErrorHandler));
      Assert.ArgumentNotNull((object)abnTestService, nameof(abnTestService));
      this._testDispatch = testDispatch;
      this._exmCampaignService = exmCampaignService;
      this._dispatchErrorHandler = dispatchErrorHandler;
      this._abnTestService = abnTestService;
    }

    [ActionName("DefaultAction")]
    public Response SupportStartTest(DispatchRequestContext data)
    {
      Assert.ArgumentNotNull((object)data, "requestArgs");
      this.CleanTestReference(data.MessageId);
      return (Response)this._dispatchErrorHandler.Execute(data, (Func<DispatchResponse>)(() => this.DoStartTest(data)));
    }

    protected void CleanTestReference(string messageId)
    {
      ABTestMessage messageItem = this._exmCampaignService.GetMessageItem(Guid.Parse(messageId)) as ABTestMessage;
      Assert.IsNotNull((object)messageItem, "current message is null");
      AbnTest abnTest = this._abnTestService.GetAbnTest(messageItem);
      if (abnTest == null || !abnTest.IsTestConfigured())
        return;
      abnTest.CleanTestReference();
    }

    protected DispatchResponse DoStartTest(DispatchRequestContext context)
    {
      Assert.IsNotNull((object)context, nameof(context));
      DispatchResponse dispatchResponse = new DispatchResponse();
      MessageItem messageItem = this._exmCampaignService.GetMessageItem(Guid.Parse(context.MessageId));
      this.Validate(context, messageItem);
      messageItem.Source.EnableNotifications = context.UseNotificationEmail;
      messageItem.Source.NotificationAddress = context.NotificationEmail;
      messageItem.Source.UsePreferredLanguage = context.UsePreferredLanguage;
      if (context.Schedule != null && context.Schedule.HasSchedule)
      {
        if (string.IsNullOrEmpty(context.Schedule.Date.Trim()))
          throw new DispatchNonLoggingException(EcmTexts.Localize("Enter a valid date and time for the scheduled delivery.", Array.Empty<object>()));
        DateTime dateTime1 = DateTime.ParseExact(context.Schedule.Date, "yyyyMMddT000000", (IFormatProvider)CultureInfo.InvariantCulture) + TimeSpan.FromSeconds(double.Parse(context.Schedule.Time));
        string id = TimeZoneInfo.FindSystemTimeZoneById(context.Schedule.TimeZone.Replace("_plus_", "+")).Id;
        DateTime dateTime2 = DateTime.MinValue;
        if (!string.IsNullOrEmpty(context.Schedule.EndDate))
        {
          DateTime exact = DateTime.ParseExact(context.Schedule.EndDate, "yyyyMMddT000000", (IFormatProvider)CultureInfo.InvariantCulture);
          dateTime2 = string.IsNullOrEmpty(context.Schedule.EndTime) ? exact.AddMinutes(10.0) : exact + TimeSpan.FromSeconds(double.Parse(context.Schedule.EndTime));
          if (dateTime1 >= dateTime2)
            throw new DispatchNonLoggingException(EcmTexts.Localize("The from date/time should be less than to date/time.", Array.Empty<object>()));
        }
        AutomaticSelectWinnerOptions automaticWinnerOptions = this.GetAutomaticWinnerOptions(context, dateTime1, id);
        this._testDispatch.Schedule(Guid.Parse(context.MessageId), (Decimal)context.AbTest.TestSize, context.AbTest.SelectedVariants, dateTime1, id, new DateTime?(dateTime2), automaticWinnerOptions, (AdvancedSchedule)null);
        dispatchResponse.NotificationMessages = new MessageBarMessageContext[1]
        {
                  new MessageBarMessageContext()
                  {
                    Message = EcmTexts.Localize("The A/B test has been scheduled.", Array.Empty<object>()),
                    ActionLink = "trigger:switch:to:report",
                    ActionText = EcmTexts.Localize("View the reports for this message.", Array.Empty<object>())
                  }
        };
        return dispatchResponse;
      }
      if (context.RecurringSchedule != null && context.RecurringSchedule.HasRecurringSchedule)
      {
        AdvancedSchedule advancedSchedule = new AdvancedSchedule()
        {
          ScheduleType = context.RecurringSchedule.ScheduleType,
          StartDate = DateTime.ParseExact(context.RecurringSchedule.StartDate, "yyyyMMddT000000", (IFormatProvider)CultureInfo.InvariantCulture) + TimeSpan.FromSeconds(double.Parse(context.RecurringSchedule.StartTime)),
          EndDate = context.RecurringSchedule.EndMode == RecurringEndMode.ByDate ? DateTime.ParseExact(context.RecurringSchedule.EndByDate, "yyyyMMddT000000", (IFormatProvider)CultureInfo.InvariantCulture) : DateTime.MaxValue,
          TimeZone = TimeZoneInfo.FindSystemTimeZoneById(context.RecurringSchedule.TimeZone.Replace("_plus_", "+")).Id,
          MaximalOccurrencesNumber = context.RecurringSchedule.EndMode == RecurringEndMode.AfterOccurrences ? context.RecurringSchedule.EndOccurrences : int.MaxValue
        };
        switch (context.RecurringSchedule.ScheduleType)
        {
          case RecurringScheduleType.Daily:
            advancedSchedule.Days = DaysOfWeek.Sunday | DaysOfWeek.Monday | DaysOfWeek.Tuesday | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday | DaysOfWeek.Saturday;
            advancedSchedule.PollInterval = TimeSpan.FromDays((double)context.RecurringSchedule.Daily.Every);
            break;
          case RecurringScheduleType.Weekly:
            advancedSchedule.Days = (DaysOfWeek)context.RecurringSchedule.Weekly.Days;
            advancedSchedule.RecurrenceInterval = context.RecurringSchedule.Weekly.Every;
            break;
          case RecurringScheduleType.Monthly:
            advancedSchedule.WeekOfMonth = (WeekOfMonth)context.RecurringSchedule.Monthly.WeekOfMonth;
            advancedSchedule.DayOfMonth = context.RecurringSchedule.Monthly.DayOfMonth;
            advancedSchedule.Days = (DaysOfWeek)context.RecurringSchedule.Monthly.Days;
            advancedSchedule.RecurrenceInterval = context.RecurringSchedule.Monthly.Every;
            break;
          case RecurringScheduleType.Yearly:
            advancedSchedule.WeekOfMonth = (WeekOfMonth)context.RecurringSchedule.Yearly.WeekOfMonth;
            advancedSchedule.DayOfMonth = context.RecurringSchedule.Yearly.DayOfMonth;
            advancedSchedule.Days = (DaysOfWeek)context.RecurringSchedule.Yearly.Days;
            advancedSchedule.Month = context.RecurringSchedule.Yearly.Month;
            advancedSchedule.RecurrenceInterval = context.RecurringSchedule.Yearly.Every;
            break;
        }
        AutomaticSelectWinnerOptions automaticWinnerOptions = this.GetAutomaticWinnerOptions(context, advancedSchedule.StartDate, advancedSchedule.TimeZone);
        this._testDispatch.Schedule(Guid.Parse(context.MessageId), (Decimal)context.AbTest.TestSize, context.AbTest.SelectedVariants, advancedSchedule.StartDate, advancedSchedule.TimeZone, new DateTime?(), automaticWinnerOptions, advancedSchedule);
        dispatchResponse.NotificationMessages = new MessageBarMessageContext[1]
        {
                  new MessageBarMessageContext()
                  {
                    Message = EcmTexts.Localize("The message delivery has been scheduled.", Array.Empty<object>()),
                    ActionLink = "trigger:switch:to:report",
                    ActionText = EcmTexts.Localize("View the reports for this message.", Array.Empty<object>())
                  }
        };
        return dispatchResponse;
      }
      AutomaticSelectWinnerOptions automaticWinnerOptions1 = this.GetAutomaticWinnerOptions(context, DateTime.MinValue);
      this._testDispatch.Start(Guid.Parse(context.MessageId), (Decimal)context.AbTest.TestSize, context.AbTest.SelectedVariants, automaticWinnerOptions1);
      dispatchResponse.NotificationMessages = new MessageBarMessageContext[1]
      {
                new MessageBarMessageContext()
                {
                  Message = EcmTexts.Localize("The A/B test has been started.", Array.Empty<object>()),
                  ActionLink = "trigger:switch:to:report",
                  ActionText = EcmTexts.Localize("View the reports for this message.", Array.Empty<object>())
                }
      };
      return dispatchResponse;
    }

    protected void Validate(DispatchRequestContext context, MessageItem messageItem)
    {
      if (context.AbTest.SelectWinnerAutomatically)
      {
        int? automaticTimeAmount = context.AbTest.AutomaticTimeAmount;
        if (!automaticTimeAmount.HasValue)
          throw new DispatchNonLoggingException(EcmTexts.Localize("Select a time period after which a winner will be automatically selected.", Array.Empty<object>()));
        automaticTimeAmount = context.AbTest.AutomaticTimeAmount;
        if (automaticTimeAmount.Value <= 0)
          throw new DispatchNonLoggingException(EcmTexts.Localize("The time after which the winner is automatically selected must be greater than 0.", Array.Empty<object>()));
      }
      if (context.AbTest.SelectedVariants.Length == 0)
        throw new DispatchNonLoggingException(EcmTexts.Localize("The A/B test has not been sent. Select one or more variants and try again.", Array.Empty<object>()));
      if (this.AnyUntranslatedVariants(messageItem, context.AbTest.SelectedVariants))
        throw new DispatchNonLoggingException(EcmTexts.Localize("Please translate all the message variants in order to start A/B test.", Array.Empty<object>()));
    }

    protected AutomaticSelectWinnerOptions GetAutomaticWinnerOptions(DispatchRequestContext context, DateTime scheduledDate, string timezone = null)
    {
      if (!context.AbTest.SelectWinnerAutomatically)
        return (AutomaticSelectWinnerOptions)null;
      scheduledDate = new ScheduledTimeConverter(timezone).FromTimeZoneToUtc(scheduledDate);
      scheduledDate = scheduledDate < DateTime.UtcNow ? DateTime.UtcNow : scheduledDate;
      int? automaticTimeAmount;
      DateTime dateTime;
      switch (context.AbTest.AutomaticTimeMode)
      {
        case 1:
          ref DateTime local1 = ref scheduledDate;
          automaticTimeAmount = context.AbTest.AutomaticTimeAmount;
          double num1 = (double)automaticTimeAmount.Value;
          dateTime = local1.AddHours(num1);
          break;
        case 2:
          ref DateTime local2 = ref scheduledDate;
          automaticTimeAmount = context.AbTest.AutomaticTimeAmount;
          double num2 = (double)automaticTimeAmount.Value;
          dateTime = local2.AddDays(num2);
          break;
        case 3:
          dateTime = scheduledDate.AddDays((double)(7 * context.AbTest.AutomaticTimeAmount.Value));
          break;
        case 4:
          ref DateTime local3 = ref scheduledDate;
          automaticTimeAmount = context.AbTest.AutomaticTimeAmount;
          int months = automaticTimeAmount.Value;
          dateTime = local3.AddMonths(months);
          break;
        default:
          ref DateTime local4 = ref scheduledDate;
          automaticTimeAmount = context.AbTest.AutomaticTimeAmount;
          double num3 = (double)automaticTimeAmount.Value;
          dateTime = local4.AddHours(num3);
          break;
      }
      AutomaticSelectWinnerOptions selectWinnerOptions1 = new AutomaticSelectWinnerOptions();
      selectWinnerOptions1.EndTime = dateTime;
      selectWinnerOptions1.WinnerSelectionMode = context.AbTest.BestValuePerVisit ? WinnerSelectionMode.ValuePerVisit : WinnerSelectionMode.OpenRate;
      AutomaticSelectWinnerOptions selectWinnerOptions2 = selectWinnerOptions1;
      string[] strArray = new string[2]
      {
                context.AbTest.AutomaticTimeMode.ToString((IFormatProvider) CultureInfo.InvariantCulture),
                null
      };
      int index = 1;
      automaticTimeAmount = context.AbTest.AutomaticTimeAmount;
      string str = automaticTimeAmount.Value.ToString((IFormatProvider)CultureInfo.InvariantCulture);
      strArray[index] = str;
      selectWinnerOptions2.Parameters = strArray;
      return selectWinnerOptions1;
    }

    protected bool AnyUntranslatedVariants(MessageItem messageItem, int[] selectedVariants)
    {
      AbnTest abnTest = this._abnTestService.GetAbnTest(messageItem);
      if (abnTest == null)
        return false;
      return abnTest.AnyUntranslatedVariants(this.GetSelectedVariants(selectedVariants, abnTest));
    }

    protected List<Item> GetSelectedVariants(int[] selectedVariants, AbnTest abnTest)
    {
      Assert.IsNotNull((object)selectedVariants, nameof(selectedVariants));
      Assert.IsNotNull((object)abnTest, nameof(abnTest));
      List<Item> objList = new List<Item>();
      foreach (int selectedVariant in selectedVariants)
      {
        if (selectedVariant < abnTest.TestCandidates.Count)
          objList.Add(abnTest.TestCandidates[selectedVariant]);
      }
      return objList;
    }

    protected enum AutomaticTimeMode
    {
      Hour = 1,
      Day = 2,
      Week = 3,
      Month = 4,
    }
  }
}