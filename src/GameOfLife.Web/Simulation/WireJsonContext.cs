using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOfLife.Web.Simulation;

/// <summary>
/// Compile-time JSON metadata for what goes over the wire, so SignalR's JSON protocol serialises
/// frames without reflection: no first-call warm-up, no name lookups, and trimming-safe. Registered
/// on the protocol's serializer options in Program.cs; unknown types still fall back to reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Frame))]
public sealed partial class WireJsonContext : JsonSerializerContext;
