using SystemX.Auth2.Client.Options;
using SystemX.Auth2.Samples.Worker;

// A background job that talks to other systems as a PROGRAM (machine to machine, "client credentials"): no person signs in, the job proves who it is
// with its client id and secret, gets a machine token, and uses it to call the Auth2 Server and another API. See README.md.
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))                      // ServerUrl, ApplicationName, ClientId, ClientSecret, ProjectKey
    .ServerWithMultiProject(builder.Configuration["Auth2:ProjectKey"] ?? "sample-project")
    .EnableMachineToMachine(o => o.Scopes = ["reports.read", "jobs.run"])         // the scopes the job asks for when a call names none
    .EnableResilience();                                                           // retry, timeout and circuit breaker for the calls that are safe to repeat

// The API the job calls (the MinimalApi sample). AddAuth2MachineToken puts "Authorization: Bearer <machine token>" on every request:
// the token is made, cached until it nearly expires, and renewed for you.
builder.Services
    .AddHttpClient(ReportJob.ApiClientName, c => c.BaseAddress = new Uri(builder.Configuration["Samples:MinimalApiUrl"] ?? "http://localhost:5102/"))
    .AddAuth2MachineToken("reports.read", "jobs.run");

builder.Services.Configure<JobOptions>(builder.Configuration.GetSection("Job"));
builder.Services.AddHostedService<ReportJob>();

await builder.Build().RunAsync();
