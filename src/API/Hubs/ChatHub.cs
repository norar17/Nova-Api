using System.Security.Claims;
using ECommerce.Application.Chat.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ECommerce.API.Hubs;

/// <summary>
/// Real-time chat hub — the ASP.NET Core equivalent of a Socket.IO namespace. Clients connect once
/// (see ChatWidget.tsx on the frontend, using @microsoft/signalr) and join one SignalR "group" per
/// conversation they open; messages sent to that group fan out to every connected participant
/// without polling. REST endpoints on ChatController cover initial history load and any client that
/// isn't running the socket (e.g. a server-to-server integration); this hub only handles the live part.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly IChatService _chatService;

    public ChatHub(IChatService chatService)
    {
        _chatService = chatService;
    }

    private Guid UserId => Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Called by the client after opening a conversation, so the server can route messages to it.</summary>
    public async Task JoinConversation(string conversationId)
    {
        if (!Guid.TryParse(conversationId, out var id))
        {
            return;
        }

        var canAccess = await _chatService.CanAccessConversationAsync(UserId, id);
        if (!canAccess.Value)
        {
            return; // silently refuse — don't leak whether the conversation id exists
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(id));
    }

    public async Task LeaveConversation(string conversationId)
    {
        if (Guid.TryParse(conversationId, out var id))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(id));
        }
    }

    /// <summary>
    /// Sends a message: persists it through IChatService (the same authorization + save logic the
    /// REST fallback uses), then broadcasts the saved message to everyone in the conversation's group.
    /// </summary>
    public async Task SendMessage(string conversationId, string content)
    {
        if (!Guid.TryParse(conversationId, out var id))
        {
            return;
        }

        var result = await _chatService.SendMessageAsync(UserId, id, content);
        if (result.IsFailure)
        {
            await Clients.Caller.SendAsync("MessageError", result.Error);
            return;
        }

        await Clients.Group(GroupName(id)).SendAsync("ReceiveMessage", result.Value);
    }

    private static string GroupName(Guid conversationId) => $"conversation:{conversationId}";
}
