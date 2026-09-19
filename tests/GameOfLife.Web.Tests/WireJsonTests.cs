using System.Text;
using System.Text.Json;
using GameOfLife.Core;
using GameOfLife.Web.Simulation;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GameOfLife.Web.Tests;

/// <summary>The source-generated JSON metadata is what SignalR uses, and it produces the same JSON as reflection did.</summary>
[Collection(WebCollection.Name)]
public sealed class WireJsonTests(WebAppFixture app)
{
    private static readonly Frame Sample = new(1234, 36, true, 10, 100, 43, CellsCodec.Encode([0, 1, 4299, 4300], 100, 43), false, false, 0, 300_000);

    [Fact]
    public void The_hub_protocol_resolves_frames_through_the_generated_context()
    {
        var options = app.Services.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;

        Assert.Contains(WireJsonContext.Default, options.TypeInfoResolverChain);
        Assert.Same(WireJsonContext.Default, options.GetTypeInfo(typeof(Frame)).OriginatingResolver);
        // Everything else still resolves: hub arguments are bound through the same options.
        Assert.NotNull(options.GetTypeInfo(typeof(long)));
    }

    [Fact]
    public void Generated_and_reflection_serialisation_agree()
    {
        var reflection = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var generated = JsonSerializer.Serialize(Sample, WireJsonContext.Default.Frame);
        var expected = JsonSerializer.Serialize(Sample, reflection);

        Assert.Equal(expected, generated);
        Assert.Contains("\"cells\":\"" + Convert.ToBase64String(Sample.Cells), generated); // byte[] is base64 in JSON
        Assert.Equal(Sample, JsonSerializer.Deserialize(Encoding.UTF8.GetBytes(generated), WireJsonContext.Default.Frame));
    }
}
