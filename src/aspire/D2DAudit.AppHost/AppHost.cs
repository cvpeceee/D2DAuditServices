var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithDataVolume().AddDatabase("ControlPlane", "d2d_control");

var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", "d2d")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "d2d_dev_password")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console");

var temporal = builder.AddContainer("temporal", "temporalio/temporal", "1.27.2")
    .WithArgs("server", "start-dev", "--ip", "0.0.0.0", "--port", "7233")
    .WithEndpoint(port: 7233, targetPort: 7233, name: "grpc", isProxied: false);

var api = builder.AddProject<Projects.D2DAudit_Api>("api")
    .WithReference(postgres)
    .WaitFor(postgres)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Storage__Endpoint", minio.GetEndpoint("s3"))
    .WithEnvironment("Storage__AccessKey", "d2d")
    .WithEnvironment("Storage__SecretKey", "d2d_dev_password")
    .WithEnvironment("Storage__Bucket", "d2d-evidence")
    .WithEnvironment("Storage__ForcePathStyle", "true")
    .WithEnvironment("Storage__ObjectLock", "false")
    .WithEnvironment("TEMPORAL_ADDRESS", "localhost:7233")
    .WaitFor(minio)
    .WaitFor(temporal);

builder.AddViteApp("web", "../../web")
    .WithEnvironment("D2D_API_PROXY", api.GetEndpoint("http"))
    .WaitFor(api);

builder.Build().Run();
