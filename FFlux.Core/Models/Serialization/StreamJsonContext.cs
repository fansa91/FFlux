using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Screenbox.Core.Models.Serialization;

/// <summary>
/// Source generated metadata for the network stream library file.
/// </summary>
/// <remarks>
/// The app ships as Native AOT, and the SDK then sets
/// <c>System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault</c> to <c>false</c>.
/// Without this context the reflection based serializer throws
/// <see cref="System.Text.Json.InvalidOperationException"/> at runtime and the library is never
/// written to disk - a failure the unit tests cannot see, because the test host still allows
/// reflection.
/// </remarks>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default, WriteIndented = true)]
[JsonSerializable(typeof(List<StreamItem>))]
[JsonSerializable(typeof(StreamItem))]
public sealed partial class StreamJsonContext : JsonSerializerContext
{
}
