// Copyright (c) David Pine. All rights reserved.
// Licensed under the MIT License.

var builder = Host.CreateApplicationBuilder(args);
Environment.SetEnvironmentVariable("INPUT_TOKEN", "9FEE481D-8CAC-5FF8-7D6E-B56C6C9D16BD");

builder.Services.AddActionProcessorServices();

var app = builder.Build();

var processor = app.Services.GetRequiredService<ProfanityProcessor>();
var context = app.Services.GetRequiredService<Context>();

await processor.ProcessProfanityAsync(context);

await app.RunAsync();