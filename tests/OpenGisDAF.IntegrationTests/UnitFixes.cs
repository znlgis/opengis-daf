using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;
using OpenGisDAF.Execution;
using OpenGisDAF.Operators;
using OpenGisDAF.PlanManagement;
using OpenGisDAF.Scheduling;
using ExecutionContext = OpenGisDAF.Core.ExecutionContext;

namespace OpenGisDAF.IntegrationTests;

/// <summary>
/// 针对代码审查阶段修复的缺陷的回归测试。
/// </summary>
public sealed class UnitFixes
{
    private static ExecutionContext NewContext() => new(
        planId: "test-plan",
        executionId: "test-exec",
        resultCache: null!,
        logger: NullLogger.Instance,
        services: null!,
        statistics: new PlanExecutionStatistics());

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

    [Fact]
    public void OperatorPool_ReregisteringOperator_RemovesOldCategoryIndex()
    {
        var pool = new OperatorPool();
        pool.Register(new FakeOperator("same", "old"));
        pool.Register(new FakeOperator("same", "new"));

        pool.GetByCategory("old").Should().BeEmpty();
        pool.GetByCategory("new").Should().ContainSingle();
        pool.GetById("same")!.Metadata.Category.Should().Be("new");
    }

    [Fact]
    public void AesConnectionEncryption_RoundTrips_WithTempKeyFile()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"opengis-daf-test-{Guid.NewGuid():N}.key");
        try
        {
            var enc = new AesConnectionEncryption(keyPath);
            const string secret = "Host=localhost;Username=postgres;****** w0rd;";

            var cipher = enc.Encrypt(secret);
            cipher.Should().NotBe(secret);

            // A separate instance reading the same key file must decrypt successfully.
            var dec = new AesConnectionEncryption(keyPath);
            dec.Decrypt(cipher).Should().Be(secret);
        }
        finally
        {
            if (File.Exists(keyPath))
                File.Delete(keyPath);
        }
    }

    [Fact]
    public void AesConnectionEncryption_EmptyString_RoundTripsToEmpty()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"opengis-daf-test-{Guid.NewGuid():N}.key");
        try
        {
            var enc = new AesConnectionEncryption(keyPath);
            enc.Encrypt(string.Empty).Should().BeEmpty();
            enc.Decrypt(string.Empty).Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(keyPath))
                File.Delete(keyPath);
        }
    }

    [Fact]
    public void AesConnectionEncryption_TamperedCipher_FailsToDecrypt()
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"opengis-daf-test-{Guid.NewGuid():N}.key");
        try
        {
            var enc = new AesConnectionEncryption(keyPath);
            var cipher = Convert.FromBase64String(enc.Encrypt("secret"));
            cipher[^1] ^= 0xFF; // flip bits in the final ciphertext byte

            var act = () => enc.Decrypt(Convert.ToBase64String(cipher));

            act.Should().Throw<System.Security.Cryptography.CryptographicException>();
        }
        finally
        {
            if (File.Exists(keyPath))
                File.Delete(keyPath);
        }
    }

    [Fact]
    public async Task ResultCache_ClearAsync_DisposesValuesAndAllowsRecompute()
    {
        // 回归: 淘汰/清理缓存时未释放 IAsyncDisposable，导致资源泄漏。
        var cache = new ResultCache();
        var tracker = new TrackingAsyncDisposable();

        var computeCount = 0;
        Task<TrackingAsyncDisposable> Factory()
        {
            computeCount++;
            return Task.FromResult(tracker);
        }

        (await cache.GetOrComputeAsync("k", Factory)).Should().BeSameAs(tracker);
        await cache.GetOrComputeAsync("k", Factory); // served from cache
        computeCount.Should().Be(1);

        await cache.ClearAsync(TestContext.Current.CancellationToken);
        tracker.DisposeCount.Should().Be(1);

        // After clearing, the value must be recomputed.
        await cache.GetOrComputeAsync("k", Factory);
        computeCount.Should().Be(2);
    }

    [Fact]
    public async Task ResultCache_InvalidateAsync_DisposesMatchingValues()
    {
        var cache = new ResultCache();
        var tracker = new TrackingAsyncDisposable();

        await cache.GetOrComputeAsync("prefix:key", () => Task.FromResult(tracker));
        await cache.InvalidateAsync("prefix:");

        tracker.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ResultCache_ReplacingExpiredValue_DisposesPreviousValue()
    {
        var cache = new ResultCache();
        var previous = new TrackingAsyncDisposable();
        var replacement = new TrackingAsyncDisposable();

        await cache.GetOrComputeAsync("replace", () => Task.FromResult(previous));
        await cache.GetOrComputeAsync("replace", () => Task.FromResult(replacement), TimeSpan.Zero);

        previous.DisposeCount.Should().Be(1);
        replacement.DisposeCount.Should().Be(0);
    }

    [Fact]
    public async Task ResultCache_ClearAsync_DoesNotDisposeActiveKeyLock()
    {
        var cache = new ResultCache();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var computation = cache.GetOrComputeAsync("active", async () =>
        {
            factoryStarted.SetResult();
            await releaseFactory.Task;
            return new object();
        });

        await factoryStarted.Task;
        var clear = cache.ClearAsync(TestContext.Current.CancellationToken);
        clear.IsCompleted.Should().BeFalse();

        releaseFactory.SetResult();
        await computation;
        await clear.WaitAsync(TestContext.Current.CancellationToken);

        var recomputed = false;
        await cache.GetOrComputeAsync("active", () =>
        {
            recomputed = true;
            return Task.FromResult(new object());
        });

        recomputed.Should().BeTrue();
    }

    [Fact]
    public async Task ResultCache_CanceledClear_DoesNotOverlapActiveOperation()
    {
        var cache = new ResultCache();
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var computation = cache.GetOrComputeAsync("active", async () =>
        {
            factoryStarted.SetResult();
            await releaseFactory.Task;
            return new object();
        });

        await factoryStarted.Task;
        using var clearCts = new CancellationTokenSource();
        var firstClear = cache.ClearAsync(clearCts.Token);
        clearCts.Cancel();
        var firstClearAct = async () => await firstClear;
        await firstClearAct.Should().ThrowAsync<OperationCanceledException>();

        var secondClear = cache.ClearAsync(TestContext.Current.CancellationToken);
        secondClear.IsCompleted.Should().BeFalse();

        releaseFactory.SetResult();
        await computation;
        await secondClear.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResultCache_CanceledClear_DisposesAllDetachedValues()
    {
        var cache = new ResultCache();
        var first = new TrackingAsyncDisposable();
        var second = new TrackingAsyncDisposable();

        await cache.GetOrComputeAsync("first", () => Task.FromResult(first));
        await cache.GetOrComputeAsync("second", () => Task.FromResult(second));

        using var clearCts = new CancellationTokenSource();
        clearCts.Cancel();

        var act = async () => await cache.ClearAsync(clearCts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task SerialScheduler_SourceCreationFailure_RecordsFailedItem()
    {
        using var host = new DafTestHost();
        var scheduler = host.Services.GetRequiredService<ISchedulingEngine>();
        var plan = new AnalysisPlan("source-failure", "Source failure", "1.0.0")
        {
            Items =
            [
                new AnalysisItem("item-1", "missing", new OutputBinding(OutputAdapterType.ConsoleWriter, "-"))
                {
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding
                        {
                            Type = BindingType.External,
                            SourceId = "unsupported.data"
                        }
                    }
                }
            ]
        };

        var stats = await scheduler.ExecuteAsync(plan, TestContext.Current.CancellationToken);

        stats.ItemStats.Should().ContainSingle(item =>
            item.ItemId == "item-1" && item.FailedCount == 1);
    }

    [Fact]
    public async Task ExecutionEngine_OperatorThatThrows_IsContainedAsFailedResult()
    {
        // 回归: 算子抛出未处理异常会中断整个计划，而非按失败策略处理。
        var pool = new OperatorPool();
        pool.Register(new ThrowingOperator("boom"));
        var engine = new ExecutionEngine(pool, new ResultCache(), NullLogger<ExecutionEngine>.Instance);

        var item = new AnalysisItem("item-1", "boom",
            new OutputBinding(OutputAdapterType.ConsoleWriter, "out"));

        var result = await engine.ExecuteItemAsync(
            item,
            new Dictionary<string, IFeatureSource>(),
            NewContext(),
            TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExecutionStatus.Failed);
        result.ErrorCode.Should().Be(ErrorCode.RtUnexpected);
    }

    [Fact]
    public async Task ExecutionEngine_OperatorCancellation_PropagatesInsteadOfBeingSwallowed()
    {
        var pool = new OperatorPool();
        pool.Register(new CancellingOperator("cancel"));
        var engine = new ExecutionEngine(pool, new ResultCache(), NullLogger<ExecutionEngine>.Instance);

        var item = new AnalysisItem("item-1", "cancel",
            new OutputBinding(OutputAdapterType.ConsoleWriter, "out"));

        var act = async () => await engine.ExecuteItemAsync(
            item,
            new Dictionary<string, IFeatureSource>(),
            NewContext(),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingOperator : IOperator
    {
        public ThrowingOperator(string id) =>
            Metadata = new OperatorMetadata
            {
                Id = id,
                Name = id,
                Category = "test",
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
            throw new InvalidOperationException("boom");
    }

    private sealed class CancellingOperator : IOperator
    {
        public CancellingOperator(string id) =>
            Metadata = new OperatorMetadata
            {
                Id = id,
                Name = id,
                Category = "test",
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
            throw new OperationCanceledException();
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
