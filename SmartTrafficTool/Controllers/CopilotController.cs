using Microsoft.AspNetCore.Mvc;
using SmartTrafficTool.Services;
using SmartTrafficTool.ViewModels;

namespace SmartTrafficTool.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CopilotController : ControllerBase
{
    private readonly ICopilotIntentService _copilot;

    public CopilotController(ICopilotIntentService copilot)
    {
        _copilot = copilot;
    }

    [HttpPost("message")]
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult<CopilotMessageResponse>> Message([FromBody] CopilotMessageRequest? request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message is required." });
        }

        var result = await _copilot.ProcessAsync(request.Message, request.VoiceInput, cancellationToken);
        return Ok(result);
    }
}
