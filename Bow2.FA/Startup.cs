using System;
using Bow2.FA.Helpers;
using System.Linq;
using System.Threading.Tasks;
using Bow2.FA.Models;
using Bow2.FA.Structures;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bow2.FA;
using System.Diagnostics;

[assembly: FunctionsStartup(typeof(Startup))]
namespace Bow2.FA
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            string connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
            builder.Services.AddDbContext<BowContext>(
                options => SqlServerDbContextOptionsExtensions.UseSqlServer(options, connectionString));
        }
    }

    public class TimerRun
    {
        private readonly BowContext context;
        private ILogger log;
        public TimerRun(BowContext context)
        {
            this.context = context;
        }

        [FunctionName("TimerRun")]
        public async Task Run([TimerTrigger("0 */5 * * * *", 
#if DEBUG
     RunOnStartup= true
#endif
            )] TimerInfo timer, ILogger log)
        {
            try
            {
                this.log = log;
                await DownloadEndpointsAsync();
            }
            catch (Exception ex)
            {
                log.LogError($"{ex}");
            }
        }

        protected async Task DownloadEndpointsAsync()
        {
            log.LogInformation($"Checking downloads... at: {DateTime.Now}");
            var endpointsToScrape = context.Endpoint.Where(w => w.State != (int)EpState.Finished).ToList();

            foreach (var endpoint in endpointsToScrape)
            {
                var data = await Scraper.ReadAsync(endpoint.Url);
                var differ = data != endpoint.Data;
                log.LogInformation($"{endpoint.Url} read end, data differs? {differ}");
                if (differ && data != null)
                {
                    endpoint.Data = data;
                    endpoint.Lastmodified = DateTime.UtcNow;
                    endpoint.State = (int)EpState.Ongoing;
                }
            }
            await context.SaveChangesAsync();
            log.LogInformation($"Download finished... at: {DateTime.Now}");
        }
    }
}
