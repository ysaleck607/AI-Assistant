using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Domain.Entities;

public sealed class Conversation
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid OwnerMemberId { get; set; }

    public string Title { get; set; } = string.Empty;

    public ConversationStatus Status { get; set; }

    public int Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public Organization Organization { get; set; } = null!;

    public OrganizationMember OwnerMember { get; set; } = null!;

    public ICollection<Message> Messages { get; set; } = [];
}
