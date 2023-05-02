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
        public TimerRun(BowContext context)
        {
            this.context = context;
        }

        [FunctionName("TimerRun")]
        public async Task Run([TimerTrigger("0 */5 * * * *", RunOnStartup = true)] TimerInfo timer, ILogger log)
        {
            try
            {
                log.LogInformation($"Timer trigger function executed at: {DateTime.Now}, Timer: {timer}");

                await ProcessEndpointsAsync(log);
            }
            catch (Exception ex)
            {
                log.LogError($"{ex}");
            }
        }

        protected async Task ProcessEndpointsAsync(ILogger log)
        {
            var endpointsToScrape = context.Endpoint.Where(w => w.State == (int)EpState.Empty).ToList();

            foreach (var endpoint in endpointsToScrape)
            {
                var content = await Scraper.ReadAsync(endpoint.Url);
                log.LogInformation($"Content: {content}");
                Debug.WriteLine("after read...");
                if (content != null)
                {
                    Debug.WriteLine($"entity set...{endpoint.Id}");

                    endpoint.Data = content;
                    endpoint.State = (int)EpState.Ongoing;
                }
            }
            Debug.WriteLine("before save...");
            await context.SaveChangesAsync();
            Debug.WriteLine("FINISHED...");
        }
    }
}
