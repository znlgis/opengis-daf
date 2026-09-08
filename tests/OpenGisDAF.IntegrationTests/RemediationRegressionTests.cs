using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using OpenGisDAF.Adapters;
using OpenGisDAF.Adapters.Utilities;
using OpenGisDAF.Core;
using OpenGisDAF.Execution;
using OpenGisDAF.PlanManagement;
using OpenGisDAF.Scheduling;

namespace OpenGisDAF.IntegrationTests;

public sealed class RemediationRegressionTests : IClassFixture<DafTestHost>
{
    private readonly DafTestHost _host;

    public RemediationRegressionTests(DafTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void DeserializePlan_NormalizesScalarParametersToClrValues()
    {
        var serializer = new PlanSerializer();
        var plan = serializer.Deserialize("""
        {
          "id": "json-parameters",
          "name": "JSON parameters",
          "version": "1.0.0",
          "items": [
            {
              "id": "step",
              "operatorId": "coordinate_transform",
              "inputs": {},
              "parameters": {
                "source_epsg": 2326,
                "target_epsg": 4326,
                "relationship": "within",
                "use_second_input": false,
                "target_field": "TEST_FIELD",
                "field_type": "Double",
                "required_fields": "FACILITY_I,NAME"
              },
              "output": { "adapterType": "console", "targetPath": "console" }
            }
          ]
        }
        """);

        var parameters = plan.Items[0].Parameters;
        parameters["source_epsg"].Should().Be(2326L);
        parameters["target_epsg"].Should().Be(4326L);
        parameters["relationship"].Should().Be("within");
        parameters["use_second_input"].Should().Be(false);
        parameters["target_field"].Should().Be("TEST_FIELD");
        parameters["field_type"].Should().Be("Double");
        parameters["required_fields"].Should().Be("FACILITY_I,NAME");
    }

    [Theory]
    [InlineData("console", OutputAdapterType.ConsoleWriter)]
    [InlineData("geojson", OutputAdapterType.GeoJsonWriter)]
    [InlineData("shapefile", OutputAdapterType.ShapefileWriter)]
    [InlineData("postgis", OutputAdapterType.PostGISWriter)]
    public void DeserializePlan_AcceptsDocumentedAdapterAliases(string adapterType, OutputAdapterType expected)
    {
        var serializer = new PlanSerializer();
        var json = """
        {
          "id": "adapter-alias",
          "name": "Adapter alias",
          "version": "1.0.0",
          "items": [{
            "id": "step",
            "operatorId": "geometry_validity_checker",
            "inputs": {},
            "output": { "adapterType": "__ADAPTER__", "targetPath": "target" }
          }]
        }
        """.Replace("__ADAPTER__", adapterType, StringComparison.Ordinal);
        var plan = serializer.Deserialize(json);

        plan.Items[0].Output!.AdapterType.Should().Be(expected);
    }

    [Fact]
    public async Task InMemoryFeatureSource_PreservesSchemaAndSpatialReference()
    {
        var sourceReference = new SpatialReference(2326, "EPSG:2326 WKT");
        var metadata = new FeatureSchemaMetadata
        {
            Fields =
            [
                new FieldDefinition { Name = "code", Type = FieldType.String, Required = true }
            ],
            GeometryType = GeometryType.Point,
            SpatialReference = sourceReference
        };
        var source = new InMemoryFeatureSource(
            [new SimpleFeature("0", new Point(1, 2), new Dictionary<string, object?> { ["code"] = "A" })],
            metadata: metadata);

        source.Should().BeAssignableTo<IFeatureSchemaProvider>();
        var schema = ((IFeatureSchemaProvider)source).Schema;
        schema.Fields.Should().ContainSingle(field => field.Name == "code" && field.Type == FieldType.String);
        schema.GeometryType.Should().Be(GeometryType.Point);
        schema.SpatialReference.Should().BeSameAs(sourceReference);
        source.SpatialReference.Should().BeSameAs(sourceReference);

        await source.DisposeAsync();
    }

    [Fact]
    public async Task Scheduler_PassesSourceSchemaToShapefileSink()
    {
        var outputPath = Path.Combine(_host.TempDir, "schema_output.shp");
        var sourcePath = _host.GetTestDataPath("incomplete-attributes.geojson");
        var plan = new AnalysisPlan
        {
            Id = "schema_output_test",
            Name = "Schema output test",
            Version = "1.0.0",
            Items =
            [
                new AnalysisItem
                {
                    Id = "read_and_write",
                    OperatorId = "geometry_validity_checker",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding
                        {
                            Type = BindingType.External,
                            SourceId = sourcePath
                        }
                    },
                    Output = new OutputBinding
                    {
                        AdapterType = OutputAdapterType.ShapefileWriter,
                        TargetPath = outputPath
                    }
                }
            ]
        };

        var stats = await _host.Services
            .GetRequiredService<ISchedulingEngine>()
            .ExecuteAsync(plan, TestContext.Current.CancellationToken);

        stats.ItemStats.Should().ContainSingle(item => item.FailedCount == 0);
        File.Exists(outputPath).Should().BeTrue();
        File.Exists(Path.ChangeExtension(outputPath, ".dbf")).Should().BeTrue();
        File.Exists(Path.ChangeExtension(outputPath, ".prj")).Should().BeTrue();

        var layerInfo = await Task.Run(() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = @"C:\OSGeo4W\bin\ogrinfo.exe",
                Arguments = $"-so -al \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!);
        var output = await layerInfo.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = await layerInfo.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await layerInfo.WaitForExitAsync(TestContext.Current.CancellationToken);

        layerInfo.ExitCode.Should().Be(0, error);
        output.Should().Contain("code: String");
        output.Should().Contain("name: String");
        output.Should().Contain("descriptio: String");
    }

    [Fact]
    public void WktConverter_PreservesThreeDimensionalCoordinates()
    {
        var geometry = new Point(10, 20, 30);

        WktConverter.ToWkt(geometry).Should().Contain("10 20 30");
    }

    [Fact]
    public async Task QualityReport_SerializesViolationGeometryAndChineseText()
    {
        var plan = new AnalysisPlan("qc-report", "QC report", "1.0.0");
        var issue = new IssueRecord
        {
            PlanId = plan.Id,
            ExecutionId = "execution",
            ItemId = "geometry",
            FeatureId = "1",
            IssueType = "GEOM_INVALID",
            Severity = IssueSeverity.Error,
            Description = "几何无效",
            ViolationGeometry = new Point(5, 6)
        };
        var reportPath = Path.Combine(_host.TempDir, "qc-report.json");

        var report = QualityReportGenerator.Generate(
            [issue],
            new Dictionary<string, List<IssueRecord>> { ["geometry"] = [issue] },
            plan);
        await QualityReportGenerator.SaveAsync(report, reportPath, TestContext.Current.CancellationToken);

        var json = await File.ReadAllTextAsync(reportPath, TestContext.Current.CancellationToken);
        json.Should().Contain("几何无效");
        json.Should().Contain("POINT (5 6)");
    }

    [Fact]
    public async Task QualityReport_IsGeneratedWhenQualityChecksFindNoIssues()
    {
        var plan = new AnalysisPlan("empty-qc", "Empty QC", "1.0.0");
        var reportPath = Path.Combine(_host.TempDir, "empty-qc-report.json");
        var report = QualityReportGenerator.Generate(
            [],
            new Dictionary<string, List<IssueRecord>>(),
            plan);

        await QualityReportGenerator.SaveAsync(report, reportPath, TestContext.Current.CancellationToken);

        File.Exists(reportPath).Should().BeTrue();
        report.TotalScore.Should().Be(100);
    }

    [Fact]
    public async Task Scheduler_ReportsZeroIssuesForSuccessfulQualityCheck()
    {
        var plan = new AnalysisPlan
        {
            Id = "zero-qc",
            Name = "Zero QC",
            Version = "1.0.0",
            Items =
            [
                new AnalysisItem
                {
                    Id = "geometry_qc",
                    OperatorId = "geometry_validity_checker",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding
                        {
                            Type = BindingType.External,
                            SourceId = _host.GetTestDataPath("points.geojson")
                        }
                    },
                    ExecutionPolicy = new ItemExecutionPolicy { QcMode = true },
                    Output = new OutputBinding
                    {
                        AdapterType = OutputAdapterType.ConsoleWriter,
                        TargetPath = "console"
                    }
                }
            ]
        };

        var stats = await _host.Services
            .GetRequiredService<ISchedulingEngine>()
            .ExecuteAsync(plan, TestContext.Current.CancellationToken);

        stats.QcStats.Should().NotBeNull();
        stats.QcStats!.TotalIssues.Should().Be(0);
        stats.ItemStats.Should().ContainSingle(item => item.FeaturesProcessed == 5);
    }

    [Fact]
    public async Task IntersectQualityCheck_ReportsIssueRecords()
    {
        var polygonsPath = _host.GetTestDataPath("polygons.geojson");
        var plan = new AnalysisPlan
        {
            Id = "intersect-qc",
            Name = "Intersect QC",
            Version = "1.0.0",
            Items =
            [
                new AnalysisItem
                {
                    Id = "intersect_qc",
                    OperatorId = "intersect_check",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding { Type = BindingType.External, SourceId = polygonsPath },
                        ["target"] = new InputBinding { Type = BindingType.External, SourceId = polygonsPath }
                    },
                    Parameters = new Dictionary<string, object?> { ["use_second_input"] = true },
                    ExecutionPolicy = new ItemExecutionPolicy { QcMode = true },
                    Output = new OutputBinding
                    {
                        AdapterType = OutputAdapterType.ConsoleWriter,
                        TargetPath = "console"
                    }
                }
            ]
        };

        var stats = await _host.Services
            .GetRequiredService<ISchedulingEngine>()
            .ExecuteAsync(plan, TestContext.Current.CancellationToken);

        stats.Issues.Should().NotBeEmpty();
        stats.Issues.Should().OnlyContain(issue => issue.IssueType == "INTERSECT");
        stats.Issues.Should().OnlyContain(issue => issue.Severity == IssueSeverity.Error);
    }

    [Fact]
    public async Task TimeoutController_DistinguishesTimeoutFromExternalCancellation()
    {
        var timeoutResult = await TimeoutController.ExecuteWithTimeoutAsync(
            async cancellationToken =>
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => completion.TrySetResult());
                await completion.Task;
                throw new OperationCanceledException(cancellationToken);
            },
            TimeSpan.FromMilliseconds(10),
            TestContext.Current.CancellationToken);

        timeoutResult.Status.Should().Be(ExecutionStatus.Failed);
        timeoutResult.ErrorCode.Should().Be(ErrorCode.RtTimeout);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledResult = await TimeoutController.ExecuteWithTimeoutAsync(
            async cancellationToken =>
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => completion.TrySetResult());
                await completion.Task;
                throw new OperationCanceledException(cancellationToken);
            },
            TimeSpan.FromMinutes(1),
            cancellation.Token);

        cancelledResult.Status.Should().Be(ExecutionStatus.Canceled);
        cancelledResult.ErrorCode.Should().Be(ErrorCode.RtCancelled);
    }

    [Fact]
    public async Task PlanValidator_IncludesCycleStartAndEndInDiagnostic()
    {
        var plan = new AnalysisPlan
        {
            Id = "cycle",
            Name = "Cycle",
            Version = "1.0.0",
            Items =
            [
                new AnalysisItem
                {
                    Id = "a",
                    OperatorId = "clip",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding { Type = BindingType.Upstream, SourceId = "b" },
                        ["clip"] = new InputBinding { Type = BindingType.Upstream, SourceId = "b" }
                    },
                    Output = new OutputBinding { AdapterType = OutputAdapterType.GeoJsonWriter, TargetPath = "a.geojson" }
                },
                new AnalysisItem
                {
                    Id = "b",
                    OperatorId = "clip",
                    Inputs = new Dictionary<string, InputBinding>
                    {
                        ["source"] = new InputBinding { Type = BindingType.Upstream, SourceId = "a" },
                        ["clip"] = new InputBinding { Type = BindingType.Upstream, SourceId = "a" }
                    },
                    Output = new OutputBinding { AdapterType = OutputAdapterType.GeoJsonWriter, TargetPath = "b.geojson" }
                }
            ]
        };

        var validator = _host.Services.GetRequiredService<IPlanValidator>();
        var result = await validator.ValidateAsync(
            plan,
            _host.Services.GetRequiredService<IOperatorPool>(),
            TestContext.Current.CancellationToken);

        result.Errors.Should().Contain(error =>
            error.Code == ErrorCode.CfgDagCycle &&
            error.Message == "Dependency cycle detected: a → b → a.");
    }

    [Fact]
    public async Task CliRun_ReturnsFailureExitCodeWhenItemFails()
    {
        var planPath = Path.Combine(_host.TempDir, "cli-failure.json");
        await File.WriteAllTextAsync(planPath, """
        {
          "id": "cli-failure",
          "name": "CLI failure",
          "version": "1.0.0",
          "items": [{
            "id": "missing-source",
            "operatorId": "geometry_validity_checker",
            "inputs": {
              "source": { "type": "external", "sourceId": "missing-file.geojson" }
            },
            "output": { "adapterType": "console", "targetPath": "console" }
          }]
        }
        """, TestContext.Current.CancellationToken);

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OpenGisDAF.Cli"))}\" --no-build -- run --plan \"{planPath}\"",
            WorkingDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task SourceFactory_CreatesStructuredPostgisSource()
    {
        var encryption = _host.Services.GetRequiredService<IConnectionEncryption>();
        var binding = new InputBinding
        {
            Type = BindingType.External,
            SourceId = "daf_test_shaft_tower",
            ConnectionConfig = new ConnectionConfig
            {
                DataSourceId = "local-postgis",
                AdapterType = "postgis",
                Host = "127.0.0.1",
                Port = 5432,
                Database = "postgres",
                UserName = "postgres",
                EncryptedPassword = encryption.Encrypt("postgres")
            }
        };

        var source = _host.Services.GetRequiredService<SourceFactory>().CreateSource(binding);
        source.Should().BeOfType<PostgisFeatureSource>();
        (await source.GetFeatureCountAsync()).Should().Be(8);
        source.Should().BeAssignableTo<IFeatureSchemaProvider>();
        ((IFeatureSchemaProvider)source).Schema.SpatialReference!.Code.Should().Be(2326);
        await source.DisposeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("postgres")]
    public void PostgisConnection_RejectsInvalidEncryptedPassword(string? encryptedPassword)
    {
        var config = new ConnectionConfig
        {
            DataSourceId = "local-postgis",
            AdapterType = "postgis",
            Host = "127.0.0.1",
            Port = 5432,
            Database = "postgres",
            UserName = "postgres",
            EncryptedPassword = encryptedPassword
        };

        var act = () => PostgisConnectionHelper.BuildConnectionString(
            config,
            _host.Services.GetRequiredService<IConnectionEncryption>());

        act.Should().Throw<ArgumentException>();
    }
}
