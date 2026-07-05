using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;

namespace OpenGisDAF.IntegrationTests;

public sealed class Scenario2_ClipAndCalculate : IClassFixture<DafTestHost>
{
    private readonly DafTestHost _host;

    public Scenario2_ClipAndCalculate(DafTestHost host) => _host = host;

    [Fact]
    public async Task ClipThenFieldCalculate_ExecutesWithoutFailure()
    {
        // Arrange
        var outputPath = Path.Combine(_host.TempDir, "clip_result.geojson");
        var polygonsPath = _host.GetTestDataPath("polygons.geojson");
        var clipPath = _host.GetTestDataPath("clip-polygon.geojson");

        var plan = new AnalysisPlan
        {
            Id = "s2_serial_test",
            Name = "Clip and Calculate Test",
            Version = "1.0.0",
            Items = new[]
            {
                new AnalysisItem
                {
                    Id = "clip_step",
                    OperatorId = "clip",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding
                        {
                            Type = BindingType.External,
                            SourceId = polygonsPath
                        },
                        ["clip"] = new InputBinding
                        {
                            Type = BindingType.External,
                            SourceId = clipPath
                        }
                    },
                    Output = new OutputBinding
                    {
                        AdapterType = "geojson",
                        TargetPath = outputPath
                    }
                },
                new AnalysisItem
                {
                    Id = "calc_step",
                    OperatorId = "field_calculator",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding
                        {
                            Type = BindingType.Upstream,
                            SourceId = "clip_step",
                            OutputKey = "output"
                        }
                    },
                    Parameters = new Dictionary<string, object?>
                    {
                        ["target_field"] = "area_label",
                        ["expression"] = "\"region: {region}\"",
                        ["field_type"] = "String"
                    },
                    Output = new OutputBinding
                    {
                        AdapterType = "console"
                    }
                }
            },
            ExecutionPolicy = new PlanExecutionPolicy
            {
                FailurePolicy = FailurePolicy.StopOnAny
            }
        };

        var scheduler = _host.Services.GetRequiredService<ISchedulingEngine>();

        // Act
        var stats = await scheduler.ExecuteAsync(plan);

        // Assert
        stats.ItemStats.Should().HaveCount(2);
        stats.ItemStats.Should().AllSatisfy(s => s.FailedCount.Should().Be(0),
            "两个串联步骤都应成功");

        File.Exists(outputPath).Should().BeTrue();
    }
}
