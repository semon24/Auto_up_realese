using AutoUpRelease.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

AgentPasswordBootstrap.WritePassword();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<AgentWorker>();

var app = builder.Build();
await app.RunAsync();
