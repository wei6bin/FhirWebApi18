
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSqlServer("sqlserver")
                .WithImageRegistry("nexus.ihis-hip.sg");
var database = db.AddDatabase("data");


var dacpac = builder.AddSqlProject("dacpac")
       .WithDacpac(Path.Combine(Path.GetDirectoryName(builder.AppHostAssembly!.Location)!, "Ihis.FhirEngine.SqlServer.v40.dacpac"))
       .WithReference(database);

builder.AddProject<Projects.Synapxe_FhirWebApi18>("server")
       .WithReference(database)
       .WaitFor(database)
       .WaitForCompletion(dacpac)
       .WithHttpsHealthCheck("/health", 200);

await builder.Build().RunAsync();
