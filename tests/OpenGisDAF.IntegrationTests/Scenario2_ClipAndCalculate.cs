using System.Text.Json;
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
        var clipOutputPath = Path.Combine(_host.TempDir, "clip_result.geojson");
        var calcOutputPath = Path.Combine(_host.TempDir, "calc_result.geojson");
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
                        AdapterType = OutputAdapterType.GeoJsonWriter,
                        TargetPath = clipOutputPath
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
                        AdapterType = OutputAdapterType.GeoJsonWriter,
                        TargetPath = calcOutputPath
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
        stats.ItemStats.Should().HaveCount(2);
        stats.ItemStats.Should().AllSatisfy(s => s.FailedCount.Should().Be(0),
            "两个串联步骤都应成功");

        File.Exists(clipOutputPath).Should().BeTrue("裁剪输出文件应存在");
        File.Exists(calcOutputPath).Should().BeTrue("字段计算输出文件应存在");

        var calcContent = await File.ReadAllTextAsync(calcOutputPath, TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(calcContent);
        var features = doc.RootElement.GetProperty("features");
        features.GetArrayLength().Should().BeGreaterThan(0, "应产出至少一个要素");

        var firstFeature = features[0];
        var props = firstFeature.GetProperty("properties");
        props.TryGetProperty("area_label", out var areaLabel).Should().BeTrue("area_label 字段应存在");
        areaLabel.GetString().Should().Be("region: North",
            "表达式应解析 region 字段值为 region: North");
    }
}
