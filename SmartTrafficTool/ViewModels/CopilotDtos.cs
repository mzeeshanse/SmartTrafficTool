using System.Text.Json.Serialization;

namespace SmartTrafficTool.ViewModels;

public class CopilotMessageRequest
{
    public string Message { get; set; } = string.Empty;

    public bool VoiceInput { get; set; }
}

public class CopilotMessageResponse
{
    public string Reply { get; set; } = string.Empty;

    public string Intent { get; set; } = string.Empty;

    public List<CopilotActionDto> Actions { get; set; } = [];

    public List<CopilotWidgetDto> Widgets { get; set; } = [];

    public bool Speak { get; set; }
}

public class CopilotActionDto
{
    public string Type { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Href { get; set; } = string.Empty;

    public bool Primary { get; set; }
}

public class CopilotWidgetDto
{
    public string Type { get; set; } = string.Empty;

    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CopilotCardItemDto>? Cards { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CopilotTableDto? Table { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CopilotChartDto? Chart { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; set; }
}

public class CopilotCardItemDto
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? Hint { get; set; }
}

public class CopilotTableDto
{
    public List<string> Headers { get; set; } = [];

    public List<List<string>> Rows { get; set; } = [];
}

public class CopilotChartDto
{
    public string Kind { get; set; } = "doughnut";

    public string? Title { get; set; }

    public List<string> Labels { get; set; } = [];

    public List<int> Data { get; set; } = [];

    public List<string>? Colors { get; set; }
}
