using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

internal static class Microsoft365SynchronizationWorkFactory
{
    public static Microsoft365SynchronizationWork? Create(
        Microsoft365Subscription subscription,
        Microsoft365Synchronization synchronization)
    {
        if (string.IsNullOrWhiteSpace(subscription.MicrosoftSubscriptionId))
        {
            return null;
        }

        return subscription.Microsoft365Source switch
        {
            Microsoft365List list => new Microsoft365SynchronizationWork(
                synchronization.Id,
                "SynchronizeList",
                subscription.MicrosoftSubscriptionId,
                list.SiteId,
                list.ListId,
                DriveId: null),
            Microsoft365Drive drive => new Microsoft365SynchronizationWork(
                synchronization.Id,
                "SynchronizeDrive",
                subscription.MicrosoftSubscriptionId,
                drive.SiteId,
                ListId: null,
                drive.DriveId),
            Microsoft365Source source when source.Kind == Microsoft365SourceKind.OutlookMailbox =>
                new Microsoft365SynchronizationWork(
                    synchronization.Id,
                    "SynchronizeOutlook",
                    subscription.MicrosoftSubscriptionId,
                    SiteId: null,
                    ListId: null,
                    DriveId: null),
            _ => null
        };
    }
}
