using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using OpenGisDAF.Adapters;
using OpenGisDAF.Core;
using OpenGisDAF.Operators;
using OpenGisDAF.PlanManagement;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.IntegrationTests;

/// <summary>
/// 针对代码审查阶段修复的缺陷的回归测试。
/// </summary>
public sealed class UnitFixes
{
    private static ExecutionContext NewContext() => new()
    {
        PlanId = "test-plan",
        ExecutionId = "test-exec",
        Logger = NullLogger.Instance,
    };

    private static SimpleFeature Feature(string id, params (string Key, object? Value)[] attrs)
    {
        var dict = attrs.ToDictionary(a => a.Key, a => a.Value);
        return new SimpleFeature(id, new Point(0, 0), dict);
    }

    private static async Task<List<IFeature>> RunCalculatorAsync(
        IFeature feature, string expression, string fieldType, string targetField = "result")
    {
        var op = new FieldCalculator();
        var source = new InMemoryFeatureSource(new[] { feature });
        var inputs = new Dictionary<string, IFeatureSource> { ["source"] = source };
        var parameters = new Dictionary<string, object?>
        {
            ["target_field"] = targetField,
            ["expression"] = expression,
            ["field_type"] = fieldType,
        };

        var result = await op.ExecuteAsync(inputs, parameters, NewContext(), CancellationToken.None);
        result.Status.Should().Be(ExecutionStatus.Success);

        var outputSource = (IFeatureSource)result.Outputs["output"]!;
        var features = new List<IFeature>();
        await foreach (var f in outputSource.GetFeaturesAsync())
            features.Add(f);
        return features;
    }

    [Fact]
    public async Task FieldCalculator_RepeatedFieldRef_IsEvaluatedAsArithmetic()
    {
        // 回归: {a}+{a} 曾因 HashSet 去重导致被误判为“纯字段引用”，只返回 a 的值。
        var features = await RunCalculatorAsync(
            Feature("1", ("a", 5)), "{a}+{a}", "Integer");

        features.Should().HaveCount(1);
        features[0].Attributes["result"].Should().Be(10);
    }

    [Fact]
    public async Task FieldCalculator_PureFieldRef_ReturnsRawValue()
    {
        var features = await RunCalculatorAsync(
            Feature("1", ("a", 42)), "{a}", "Integer");

        features[0].Attributes["result"].Should().Be(42);
    }

    [Fact]
    public void TimeSpanConverter_InvalidValue_ThrowsJsonException()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new TimeSpanConverter());

        var act = () => JsonSerializer.Deserialize<TimeSpan>("\"not-a-timespan\"", options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void TimeSpanConverter_RoundTrips_WithInvariantCulture()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new TimeSpanConverter());

        var json = JsonSerializer.Serialize(TimeSpan.FromSeconds(90), options);
        var parsed = JsonSerializer.Deserialize<TimeSpan>(json, options);

        parsed.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void OperatorPool_ConcurrentRegistration_IsThreadSafe()
    {
        var pool = new OperatorPool();
        var operators = Enumerable.Range(0, 200)
            .Select(i => new FakeOperator($"op_{i}", "shared_category"))
            .ToArray();

        Parallel.ForEach(operators, op => pool.Register(op));

        var grouped = pool.GetAllGroupedByCategory();
        grouped["shared_category"].Should().HaveCount(200);
        pool.GetByCategory("shared_category").Should().HaveCount(200);
    }

    private sealed class FakeOperator : IOperator
    {
        public FakeOperator(string id, string category) =>
            Metadata = new OperatorMetadata
            {
                Id = id,
                Name = id,
                Category = category,
                Version = "1.0.0",
            };

        public OperatorMetadata Metadata { get; }

        public ValidationResult Validate(AnalysisItem config) =>
            new() { Errors = [], Warnings = [] };

        public Task<ExecutionResult> ExecuteAsync(
            IReadOnlyDictionary<string, IFeatureSource> inputs,
            IReadOnlyDictionary<string, object?> parameters,
            ExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ExecutionResult { Status = ExecutionStatus.Success });
    }
}
