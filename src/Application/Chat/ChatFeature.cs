using MongoDB.Driver.Linq;
using ECommerce.Domain.Entities;
using ECommerce.Domain.Interfaces;
using ECommerce.Shared;

namespace ECommerce.Application.Chat.DTOs
{
    public record ConversationDto(
        Guid Id, Guid StoreId, string StoreName, string? StoreLogoUrl,
        string? LastMessagePreview, DateTime LastMessageAtUtc, int UnreadCount);

    public record MessageDto(Guid Id, Guid ConversationId, Guid SenderId, string SenderName, string Content, bool IsRead, DateTime CreatedAtUtc);

    public record SendMessageRequest(string Content);
    public record StartConversationRequest(Guid StoreId);
}

namespace ECommerce.Application.Chat.Interfaces
{
    using ECommerce.Application.Chat.DTOs;

    public interface IChatService
    {
        Task<Result<IReadOnlyList<ConversationDto>>> GetMyConversationsAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Result<IReadOnlyList<MessageDto>>> GetMessagesAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken = default);
        Task<Result<ConversationDto>> StartConversationAsync(Guid buyerId, Guid storeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists a message and returns it, after verifying the sender is either the conversation's
        /// buyer or the owner of its store. Used by both the REST fallback and the SignalR hub so the
        /// authorization + persistence logic exists in exactly one place.
        /// </summary>
        Task<Result<MessageDto>> SendMessageAsync(Guid senderId, Guid conversationId, string content, CancellationToken cancellationToken = default);

        Task<Result<bool>> CanAccessConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken = default);
        Task<Result> MarkReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken = default);
    }
}

namespace ECommerce.Application.Chat.Services
{
    using ECommerce.Application.Chat.DTOs;
    using ECommerce.Application.Chat.Interfaces;

    public class ChatService : IChatService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> _userManager;

        public ChatService(IUnitOfWork unitOfWork, Microsoft.AspNetCore.Identity.UserManager<Domain.Entities.ApplicationUser> userManager)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
        }

        public async Task<Result<IReadOnlyList<ConversationDto>>> GetMyConversationsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            // A user's conversations are ones where they're the buyer, OR ones belonging to a store they own.
            var myStoreIds = await _unitOfWork.Repository<Store>().Query()
                .Where(s => s.OwnerId == userId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var conversations = await _unitOfWork.Repository<Conversation>().Query()
                .Where(c => c.BuyerId == userId || myStoreIds.Contains(c.StoreId))
                .OrderByDescending(c => c.LastMessageAtUtc)
                .ToListAsync(cancellationToken);

            if (conversations.Count == 0)
            {
                return Result.Success<IReadOnlyList<ConversationDto>>(new List<ConversationDto>());
            }

            // Replaces `.Include(c => c.Store)` and `.Include(c => c.Messages...Take(1))`: batch
            // resolve the stores, and pull the most recent message per conversation from the
            // Messages collection directly instead of relying on a nested Include projection.
            var convIds = conversations.Select(c => c.Id).ToList();
            var storeIds = conversations.Select(c => c.StoreId).Distinct().ToList();
            var stores = (await _unitOfWork.Repository<Store>().GetByIdsAsync(storeIds, cancellationToken)).ToDictionary(s => s.Id);
            var lastMessageByConversation = (await _unitOfWork.Repository<Message>()
                    .FindAsync(m => convIds.Contains(m.ConversationId), cancellationToken))
                .GroupBy(m => m.ConversationId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreatedAtUtc).First());

            var dtos = new List<ConversationDto>();
            foreach (var c in conversations)
            {
                var unread = await _unitOfWork.Repository<Message>().Query()
                    .CountAsync(m => m.ConversationId == c.Id && !m.IsRead && m.SenderId != userId, cancellationToken);

                stores.TryGetValue(c.StoreId, out var store);
                lastMessageByConversation.TryGetValue(c.Id, out var lastMessage);
                dtos.Add(new ConversationDto(c.Id, c.StoreId, store?.Name ?? string.Empty, store?.LogoUrl, lastMessage?.Content, c.LastMessageAtUtc, unread));
            }

            return Result.Success<IReadOnlyList<ConversationDto>>(dtos);
        }

        public async Task<Result<IReadOnlyList<MessageDto>>> GetMessagesAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken = default)
        {
            var canAccess = await CanAccessConversationAsync(userId, conversationId, cancellationToken);
            if (!canAccess.Value)
            {
                return Result.NotFound<IReadOnlyList<MessageDto>>("Conversation", conversationId);
            }

            var messages = await _unitOfWork.Repository<Message>().Query()
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var senderIds = messages.Select(m => m.SenderId).Distinct().ToList();
            var senders = (await _userManager.Users.Where(u => senderIds.Contains(u.Id)).ToListAsync(cancellationToken))
                .ToDictionary(u => u.Id);

            var dtos = messages.Select(m =>
            {
                senders.TryGetValue(m.SenderId, out var sender);
                return new MessageDto(m.Id, m.ConversationId, m.SenderId, sender?.FullName ?? "User", m.Content, m.IsRead, m.CreatedAtUtc);
            }).ToList();

            return Result.Success<IReadOnlyList<MessageDto>>(dtos);
        }

        public async Task<Result<ConversationDto>> StartConversationAsync(Guid buyerId, Guid storeId, CancellationToken cancellationToken = default)
        {
            var store = await _unitOfWork.Repository<Store>().GetByIdAsync(storeId, cancellationToken);
            if (store is null)
            {
                return Result.NotFound<ConversationDto>("Store", storeId);
            }

            if (store.OwnerId == buyerId)
            {
                return Result.Failure<ConversationDto>("You can't message your own store.", "SELF_MESSAGE");
            }

            var existing = await _unitOfWork.Repository<Conversation>()
                .FindOneAsync(c => c.BuyerId == buyerId && c.StoreId == storeId, cancellationToken);

            if (existing is not null)
            {
                return Result.Success(new ConversationDto(existing.Id, storeId, store.Name, store.LogoUrl, null, existing.LastMessageAtUtc, 0));
            }

            var conversation = new Conversation { BuyerId = buyerId, StoreId = storeId };
            await _unitOfWork.Repository<Conversation>().AddAsync(conversation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new ConversationDto(conversation.Id, storeId, store.Name, store.LogoUrl, null, conversation.LastMessageAtUtc, 0));
        }

        public async Task<Result<MessageDto>> SendMessageAsync(Guid senderId, Guid conversationId, string content, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return Result.Failure<MessageDto>("Message cannot be empty.", "EMPTY_MESSAGE");
            }

            var canAccess = await CanAccessConversationAsync(senderId, conversationId, cancellationToken);
            if (!canAccess.Value)
            {
                return Result.NotFound<MessageDto>("Conversation", conversationId);
            }

            var conversation = (await _unitOfWork.Repository<Conversation>().GetByIdAsync(conversationId, cancellationToken))!;

            var message = new Message
            {
                ConversationId = conversationId,
                SenderId = senderId,
                Content = content.Trim(),
            };

            await _unitOfWork.Repository<Message>().AddAsync(message, cancellationToken);

            conversation.LastMessageAtUtc = DateTime.UtcNow;
            _unitOfWork.Repository<Conversation>().Update(conversation);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var sender = await _userManager.FindByIdAsync(senderId.ToString());

            return Result.Success(new MessageDto(message.Id, conversationId, senderId, sender?.FullName ?? "User", message.Content, false, message.CreatedAtUtc));
        }

        public async Task<Result<bool>> CanAccessConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken = default)
        {
            var conversation = await _unitOfWork.Repository<Conversation>()
                .FindOneAsync(c => c.Id == conversationId, cancellationToken);

            if (conversation is null)
            {
                return Result.Success(false);
            }

            if (conversation.BuyerId == userId)
            {
                return Result.Success(true);
            }

            var store = await _unitOfWork.Repository<Store>().GetByIdAsync(conversation.StoreId, cancellationToken);
            return Result.Success(store?.OwnerId == userId);
        }

        public async Task<Result> MarkReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken = default)
        {
            var canAccess = await CanAccessConversationAsync(userId, conversationId, cancellationToken);
            if (!canAccess.Value)
            {
                return Result.NotFound("Conversation", conversationId);
            }

            var unreadMessages = await _unitOfWork.Repository<Message>().Query(asNoTracking: false)
                .Where(m => m.ConversationId == conversationId && !m.IsRead && m.SenderId != userId)
                .ToListAsync(cancellationToken);

            foreach (var message in unreadMessages)
            {
                message.IsRead = true;
                _unitOfWork.Repository<Message>().Update(message);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}
