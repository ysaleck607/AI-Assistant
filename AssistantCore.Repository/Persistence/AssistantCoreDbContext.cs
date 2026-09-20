using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AssistantCore.Repository.Persistence;

public class AssistantCoreDbContext(
    DbContextOptions<AssistantCoreDbContext> options,
    IFieldEncryptorFactory? fieldEncryptorFactory = null) : DbContext(options)
{
    // EF Core caches the compiled model per DbContext type by default, ignoring constructor
    // arguments. In production this is fine (IFieldEncryptorFactory is a singleton for the app's
    // lifetime), but without this override, whichever encryptor is used by the first-constructed
    // instance in a process would silently stick to every later instance too.
    internal readonly IFieldEncryptorFactory EncryptorFactory =
        fieldEncryptorFactory ?? PassthroughFieldEncryptorFactory.Instance;

    private static readonly Type[] EncryptedConfigurationTypes =
    [
        typeof(MessageConfiguration),
        typeof(ConversationConfiguration),
        typeof(MessageSourceConfiguration),
        typeof(MessageWarningConfiguration),
        typeof(Microsoft365ListItemWorkConfiguration),
        typeof(Microsoft365SourceConfiguration),
        typeof(Microsoft365DocumentWorkConfiguration),
        typeof(Microsoft365IndexedContentConfiguration),
        typeof(OrganizationMemberConfiguration)
    ];

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();

    public DbSet<OrganizationConnector> OrganizationConnectors => Set<OrganizationConnector>();

    public DbSet<OrganizationConnectorSource> OrganizationConnectorSources => Set<OrganizationConnectorSource>();

    public DbSet<Microsoft365Connection> Microsoft365Connections => Set<Microsoft365Connection>();

    public DbSet<Microsoft365Source> Microsoft365Sources => Set<Microsoft365Source>();

    public DbSet<Microsoft365Site> Microsoft365Sites => Set<Microsoft365Site>();

    public DbSet<Microsoft365Drive> Microsoft365Drives => Set<Microsoft365Drive>();

    public DbSet<Microsoft365List> Microsoft365Lists => Set<Microsoft365List>();

    public DbSet<Microsoft365Subscription> Microsoft365Subscriptions => Set<Microsoft365Subscription>();

    public DbSet<Microsoft365Synchronization> Microsoft365Synchronizations => Set<Microsoft365Synchronization>();

    public DbSet<Microsoft365ReindexOperation> Microsoft365ReindexOperations =>
        Set<Microsoft365ReindexOperation>();

    public DbSet<Microsoft365ListItemWork> Microsoft365ListItemWorks => Set<Microsoft365ListItemWork>();

    public DbSet<Microsoft365DocumentWork> Microsoft365DocumentWorks => Set<Microsoft365DocumentWork>();

    public DbSet<Microsoft365IndexedContent> Microsoft365IndexedContents => Set<Microsoft365IndexedContent>();

    public DbSet<Microsoft365IndexedPassage> Microsoft365IndexedPassages => Set<Microsoft365IndexedPassage>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationPurgeRequest> ConversationPurgeRequests =>
        Set<ConversationPurgeRequest>();

    public DbSet<PurgeOperation> PurgeOperations => Set<PurgeOperation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<MessageSource> MessageSources => Set<MessageSource>();

    public DbSet<MessageWarning> MessageWarnings => Set<MessageWarning>();

    public DbSet<AdministrativeAuditEntry> AdministrativeAuditEntries => Set<AdministrativeAuditEntry>();

    public DbSet<OperationalIncident> OperationalIncidents => Set<OperationalIncident>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, EncryptionAwareModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AssistantCoreDbContext).Assembly,
            configurationType => !EncryptedConfigurationTypes.Contains(configurationType));

        modelBuilder.ApplyConfiguration(new MessageConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Conversations.MessageContent.v1")));
        modelBuilder.ApplyConfiguration(new ConversationConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Conversations.Title.v1")));
        modelBuilder.ApplyConfiguration(new MessageSourceConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Conversations.MessageSource.Title.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Conversations.MessageSource.Reference.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Conversations.MessageSource.Url.v1")));
        modelBuilder.ApplyConfiguration(new MessageWarningConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Conversations.MessageWarningContent.v1")));

        modelBuilder.ApplyConfiguration(new Microsoft365ListItemWorkConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.ListItemWork.WebUrl.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.ListItemWork.FieldsJson.v1")));
        modelBuilder.ApplyConfiguration(new Microsoft365SourceConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.Source.DisplayName.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.Source.WebUrl.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.Source.DeltaLink.v1")));
        modelBuilder.ApplyConfiguration(new Microsoft365DocumentWorkConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.DocumentWork.Name.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.DocumentWork.WebUrl.v1")));
        modelBuilder.ApplyConfiguration(new Microsoft365IndexedContentConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.IndexedContent.SiteUrl.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.IndexedContent.Title.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Microsoft365.IndexedContent.WebUrl.v1")));

        modelBuilder.ApplyConfiguration(new OrganizationMemberConfiguration(
            EncryptorFactory.CreateFor("AssistantCore.Members.Name.v1"),
            EncryptorFactory.CreateFor("AssistantCore.Members.Email.v1")));
    }
}
