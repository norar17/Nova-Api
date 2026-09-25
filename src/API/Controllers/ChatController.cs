using System.Security.Claims;
using ECommerce.Application.Chat.DTOs;
using ECommerce.Application.Chat.Interfaces;
using ECommerce.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.API.Controllers;

[ApiController]
[Route("api/v1/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    public ChatController(IChatService chatService) => _chatService = chatService;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("conversations")]
    public async Task<IActionResult> GetMyConversations(CancellationToken cancellationToken)
    {
        var result = await _chatService.GetMyConversationsAsync(UserId, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<ConversationDto>>.Ok(result.Value));
    }

    [HttpPost("conversations")]
    public async Task<IActionResult> StartConversation([FromBody] StartConversationRequest request, CancellationToken cancellationToken)
    {
        var result = await _chatService.StartConversationAsync(UserId, request.StoreId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ConversationDto>.Ok(result.Value))
            : BadRequest(ApiResponse<ConversationDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpGet("conversations/{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken cancellationToken)
    {
        var result = await _chatService.GetMessagesAsync(UserId, id, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<IReadOnlyList<MessageDto>>.Ok(result.Value))
            : NotFound(ApiResponse<IReadOnlyList<MessageDto>>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("conversations/{id:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        // REST fallback for clients not connected via SignalR — the hub is the primary path for live chat.
        var result = await _chatService.SendMessageAsync(UserId, id, request.Content, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<MessageDto>.Ok(result.Value))
            : BadRequest(ApiResponse<MessageDto>.Fail(result.Error!, result.ErrorCode));
    }

    [HttpPost("conversations/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var result = await _chatService.MarkReadAsync(UserId, id, cancellationToken);
        return result.IsSuccess ? Ok(ApiResponse<object>.Ok(new { })) : NotFound(ApiResponse<object>.Fail(result.Error!, result.ErrorCode));
    }
}
