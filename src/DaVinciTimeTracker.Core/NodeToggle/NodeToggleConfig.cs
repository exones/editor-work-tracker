using System.Text.Json.Serialization;

namespace DaVinciTimeTracker.Core.NodeToggle;

public enum NodeActionType
{
    /// <summary>Toggle nodes on/off (existing behaviour).</summary>
    Toggle,
    /// <summary>Navigate to a specific node in the Color page (select/activate it).</summary>
    Select
}

public enum NodeLevel
{
    /// <summary>Timeline-level node graph. Accessible via DaVinci scripting API.</summary>
    Timeline,
    /// <summary>Clip-level node graph. Accessible via DaVinci scripting API.</summary>
    Clip,
    /// <summary>Pre-clip nodes via ColorGroup.GetPreClipNodeGraph(). Clip must be assigned to a Color Group.</summary>
    PreClip,
    /// <summary>Post-clip nodes via ColorGroup.GetPostClipNodeGraph(). Clip must be assigned to a Color Group.</summary>
    PostClip,
}

public class NodeTarget
{
    [JsonPropertyName("nodeId")]
    public int? NodeId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("level")]
    public NodeLevel Level { get; set; } = NodeLevel.Timeline;

    [JsonIgnore]
    public bool IsValid => NodeId.HasValue || !string.IsNullOrWhiteSpace(Title);

    [JsonIgnore]
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Title) ? Title :
        NodeId.HasValue ? $"Node #{NodeId}" :
        "(invalid)";
}

public class ToggleGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "";

    [JsonPropertyName("actionType")]
    public NodeActionType ActionType { get; set; } = NodeActionType.Toggle;

    [JsonPropertyName("nodes")]
    public List<NodeTarget> Nodes { get; set; } = [];

    /// <summary>
    /// Last known enabled state, tracked in memory after each toggle.
    /// null = unknown (will be determined on first execute).
    /// Not persisted to JSON. Only meaningful for Toggle groups.
    /// </summary>
    [JsonIgnore]
    public bool? CurrentEnabled { get; set; }
}

public class NodeToggleConfigFile
{
    [JsonPropertyName("groups")]
    public List<ToggleGroup> Groups { get; set; } = [];
}

/// <summary>Node entry returned by the "list" action from DaVinci Resolve.</summary>
public class AvailableNode
{
    [JsonPropertyName("nodeId")]
    public int? NodeId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("level")]
    public string Level { get; set; } = "Timeline";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
