using ECommerce.Domain.Common;

namespace ECommerce.Domain.Entities;

/// <summary>
/// A single chat thread between one buyer and one store. Unique per (BuyerId, StoreId) pair —
/// re-opening chat with the same store continues the existing thread instead of creating a new one.
/// </summary>
public class Conversation : BaseEntity
{
    public Guid BuyerId { get; set; }
    public ApplicationUser Buyer { get; set; } = null!;

    public Guid StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}

public class Message : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public Guid SenderId { get; set; }
    public ApplicationUser Sender { get; set; } = null!;

    public string Content { get; set; } = string.Empty;
    public bool IsRead { get; set; }
}
