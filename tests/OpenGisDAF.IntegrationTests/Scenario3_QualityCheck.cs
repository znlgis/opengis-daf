using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenGisDAF.Core;

namespace OpenGisDAF.IntegrationTests;

public sealed class Scenario3_QualityCheck : IClassFixture<DafTestHost>
{
    private readonly DafTestHost _host;

    public Scenario3_QualityCheck(DafTestHost host) => _host = host;

    [Fact]
    public async Task AttributeCompletenessCheck_DetectsMissingAndEmptyFields()
    {
        // Arrange
        var sourcePath = _host.GetTestDataPath("incomplete-attributes.geojson");

        var plan = new AnalysisPlan
        {
            Id = "s3_qc_test",
            Name = "Quality Check Test",
            Version = "1.0.0",
            Items = new[]
            {
                new AnalysisItem
                {
                    Id = "qc_step",
                    OperatorId = "attribute_completeness_checker",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding
                        {
                            Type = BindingType.External,
                            SourceId = sourcePath
                        }
                    },
                    Parameters = new Dictionary<string, object?>
                    {
                        ["required_fields"] = "code,name,description"
                    },
                    Output = new OutputBinding
                    {
                        AdapterType = "console"
                    },
                    ExecutionPolicy = new ItemExecutionPolicy
                    {
                        QcMode = true
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
        stats.QcStats.Should().NotBeNull("QC 统计应存在");
        stats.QcStats!.TotalIssues.Should().BeGreaterThanOrEqualTo(3,
            "incomplete-attributes.geojson 中有至少 3 个空字段（fid=2 code null, fid=3 name null, fid=4 code+name null+desc empty）");

        stats.Issues.Should().NotBeEmpty();
        stats.Issues.Should().Contain(i => i.FeatureId == "2" && i.IssueType == "ATTR_EMPTY");
        stats.Issues.Should().Contain(i => i.FeatureId == "4" && i.IssueType == "ATTR_EMPTY");
    }
}
