using CyberManager.Server.Common;
using CyberManager.Server.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ============================================================================
//  CyberManagerServerService : service Windows hôte (session 0, démarrage automatique).
//  Il supervise Caddy, les workers php-cgi, le planificateur Laravel et la découverte UDP.
//  Test en console (développement) : CyberManagerServerService.exe
// ============================================================================

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = Constantes.NomService);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(45));
builder.Logging.ClearProviders();   // notre journal (ProgramData\CyberManager\logs\service-AAAAMMJJ.log) suffit

builder.Services.AddSingleton<JobObject>();
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<ClockGuard>();
builder.Services.AddSingleton<DatabaseGuard>();
builder.Services.AddSingleton<ChildProcessManager>();
builder.Services.AddSingleton<HealthMonitor>();
builder.Services.AddSingleton<EtatSuperviseur>();
builder.Services.AddSingleton<PipeServer>();
builder.Services.AddHostedService<SupervisorWorker>();

await builder.Build().RunAsync();