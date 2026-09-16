using AutoFixture;
using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Service.Tests;

internal sealed class DomainCustomization : ICustomization
{
    public void Customize(IFixture fixture)
    {
        foreach (var behavior in fixture.Behaviors
                     .OfType<ThrowingRecursionBehavior>()
                     .ToArray())
        {
            fixture.Behaviors.Remove(behavior);
        }

        fixture.Behaviors.Add(new OmitOnRecursionBehavior());
        fixture.Customize<Organization>(composer =>
            composer
                .Without(organization => organization.Members)
                .Without(organization => organization.Connectors)
                .Without(organization => organization.Microsoft365Connections));
        fixture.Customize<OrganizationMember>(composer =>
            composer.Without(member => member.Organization));
        fixture.Customize<OrganizationConnector>(composer => composer
            .Without(connector => connector.Organization)
            .Without(connector => connector.Sources)
            .Without(connector => connector.Microsoft365Connection));
        fixture.Customize<OrganizationConnectorSource>(composer =>
            composer.Without(source => source.OrganizationConnector));
        fixture.Customize<Microsoft365Connection>(composer => composer
            .Without(connection => connection.Organization)
            .Without(connection => connection.OrganizationConnector)
            .Without(connection => connection.Sources));
        fixture.Customize<Microsoft365Source>(composer => composer
            .Without(source => source.Microsoft365Connection)
            .Without(source => source.Subscriptions)
            .Without(source => source.Synchronizations));
        fixture.Customize<Microsoft365Site>(composer => composer
            .Without(site => site.Organization)
            .Without(site => site.OrganizationConnector)
            .Without(site => site.Microsoft365Connection)
            .Without(site => site.Subscriptions)
            .Without(site => site.Synchronizations));
        fixture.Customize<Microsoft365Drive>(composer => composer
            .Without(drive => drive.Organization)
            .Without(drive => drive.OrganizationConnector)
            .Without(drive => drive.Microsoft365Connection)
            .Without(drive => drive.Subscriptions)
            .Without(drive => drive.Synchronizations));
        fixture.Customize<Microsoft365List>(composer => composer
            .Without(list => list.Organization)
            .Without(list => list.OrganizationConnector)
            .Without(list => list.Microsoft365Connection)
            .Without(list => list.Subscriptions)
            .Without(list => list.Synchronizations));
        fixture.Customize<Microsoft365Subscription>(composer =>
            composer.Without(subscription => subscription.Microsoft365Source));
        fixture.Customize<Microsoft365Synchronization>(composer => composer
            .Without(synchronization => synchronization.Microsoft365Source)
            .Without(synchronization => synchronization.Microsoft365ReindexOperation));
        fixture.Customize<Microsoft365ReindexOperation>(composer => composer
            .Without(operation => operation.Organization)
            .Without(operation => operation.Microsoft365Connection)
            .Without(operation => operation.Synchronizations));
        fixture.Customize<Microsoft365ListItemWork>(composer => composer
            .Without(work => work.Organization)
            .Without(work => work.Microsoft365Source)
            .Without(work => work.Microsoft365Synchronization));
        fixture.Customize<Conversation>(composer => composer
            .Without(conversation => conversation.Organization)
            .Without(conversation => conversation.OwnerMember)
            .Without(conversation => conversation.Messages)
            .Without(conversation => conversation.DeletedAt));
        fixture.Customize<Message>(composer => composer
            .Without(message => message.Conversation)
            .Without(message => message.Sources)
            .Without(message => message.Warnings));
        fixture.Customize<MessageSource>(composer =>
            composer.Without(source => source.Message));
        fixture.Customize<MessageWarning>(composer =>
            composer.Without(warning => warning.Message));
    }
}
