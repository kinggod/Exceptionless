﻿using System;
using System.Threading.Tasks;
using Exceptionless;
using Exceptionless.Core;
using Exceptionless.Insulation.Jobs;
using Foundatio.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace OrganizationSnapshotJob {
    public class Program {
        public static async Task<int> Main() {
            try {
                var serviceProvider = JobServiceProvider.GetServiceProvider();

                if (!Settings.Current.EnableSnapshotJobs) {
                    Log.Logger.Information("Snapshot Jobs are currently disabled.");
                    return 0;
                }

                var job = serviceProvider.GetService<Exceptionless.Core.Jobs.Elastic.OrganizationSnapshotJob>();
                return await new JobRunner(job, serviceProvider.GetRequiredService<ILoggerFactory>(), runContinuous: false).RunInConsoleAsync();
            } catch (Exception ex) {
                Log.Fatal(ex, "Job terminated unexpectedly");
                return 1;
            } finally {
                Log.CloseAndFlush();
                await ExceptionlessClient.Default.ProcessQueueAsync();
            }
        }
    }
}
