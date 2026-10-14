using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public sealed class NoulCriteria
{
    private object? _true;
    private object? _false;
    internal bool HasTrue { get; private set; }
    internal bool HasFalse { get; private set; }
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? True { get => _true; init { _true = value; HasTrue = true; } }

    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? False { get => _false; init { _false = value; HasFalse = true; } }
}