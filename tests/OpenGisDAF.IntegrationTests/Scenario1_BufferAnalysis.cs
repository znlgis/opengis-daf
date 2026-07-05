using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;

namespace OpenGisDAF.IntegrationTests;

public sealed class Scenario1_BufferAnalysis : IClassFixture<DafTestHost>
{
    private readonly DafTestHost _host;

    public Scenario1_BufferAnalysis(DafTestHost host) => _host = host;

    [Fact]
    public async Task BufferPointsToGeoJson_ProducesOutputFile()
    {
        // Arrange
        var outputPath = Path.Combine(_host.TempDir, "buffer_output.geojson");
        var pointsPath = _host.GetTestDataPath("points.geojson");

        var plan = new AnalysisPlan
        {
            Id = "s1_buffer_test",
            Name = "Buffer Analysis Test",
            Version = "1.0.0",
            Items = new[]
            {
                new AnalysisItem
                {
                    Id = "buffer_step",
                    OperatorId = "buffer",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding
                        {
                            Type = BindingType.External,
                            SourceId = pointsPath
                        }
                    },
                    Parameters = new Dictionary<string, object?>
                    {
                        ["distance"] = 0.1
                    },
                    Output = new OutputBinding
                    {
                        AdapterType = OutputAdapterType.GeoJsonWriter,
                        TargetPath = outputPath
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
        var stats = await scheduler.ExecuteAsync(plan, TestContext.Current.CancellationToken);

        // Assert
        stats.ItemStats.Should().HaveCount(1);
        stats.ItemStats[0].FailedCount.Should().Be(0);

        File.Exists(outputPath).Should().BeTrue("GeoJSON 输出文件应存在");

        var content = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
        content.Should().Contain("\"Polygon\"", "缓冲区结果应为面几何");

        using var doc = JsonDocument.Parse(content);
        var features = doc.RootElement.GetProperty("features");
        features.GetArrayLength().Should().BeGreaterThan(0, "应产出至少一个要素");
    }
}
