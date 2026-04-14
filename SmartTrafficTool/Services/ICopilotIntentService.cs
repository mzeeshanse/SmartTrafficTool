using SmartTrafficTool.ViewModels;

namespace SmartTrafficTool.Services;

public interface ICopilotIntentService
{
    Task<CopilotMessageResponse> ProcessAsync(string message, bool voiceInput, CancellationToken cancellationToken = default);
}
