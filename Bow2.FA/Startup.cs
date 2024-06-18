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
                await ParseHTMLDataAsync();
            }
            catch (Exception ex)
            {
                log.LogError($"{ex}");
            }
        }

        protected async Task DownloadEndpointsAsync()
        {
            var endpointsToScrape = await context.Endpoint.Where(w => w.State == (int)EState.ToDownload).ToListAsync();
            log.LogInformation($"Downloading endpoints... at: {DateTime.Now}, {endpointsToScrape.Count}");

            foreach (var endpoint in endpointsToScrape)
            {
                var data = await Scraper.ReadAsync(endpoint.Url);
                var differ = data.Length > (endpoint.Data?.Length ?? 0);
                log.LogInformation($"{endpoint.Url} read end, data differs? {differ}, {data.Length} vs {endpoint.Data?.Length ?? 0}");
                if (differ && data != null)
                {
                    endpoint.Data = data;
                    endpoint.Lastmodified = DateTime.UtcNow;
                }
                if (differ || endpoint.Data.Length > 0)
                {
                    endpoint.State = (int)EState.ToParse;
                }
            }
            await context.SaveChangesAsync();
        }

        protected async Task ParseHTMLDataAsync()
        {
            var endpointsToScrape = await context.Endpoint.Where(w => w.State == (int)EState.ToParse).ToListAsync();
            log.LogInformation($"Parsing html... at: {DateTime.Now}, {endpointsToScrape.Count}");

            foreach (var endpoint in endpointsToScrape)
            {
                log.LogInformation($"Processing {endpoint.Url}...");
                // liga
                var league = HtmlParser.GetLeague(endpoint.Url);
                var dbLeague = await context.League.FirstOrDefaultAsync(f => f.Name == league.Name && f.Sport == league.Sport && f.Country == league.Country) ?? league;

                // teamy
                var dbTeams = await context.Team.Where(w => w.Sport == league.Sport && w.Country == league.Country).ToListAsync();

                // zapasy
                var dbMatches = await context.Match.Where(w => w.IdLeague == dbLeague.Id).AsTracking().ToListAsync();
                var origCodes = dbMatches.Select(s => s.Code).ToList();

                dbMatches = HtmlParser.GetMatches(endpoint, dbMatches, dbTeams, dbLeague);
                if (dbMatches != null)
                {
                    var newCodes = dbMatches.Select(s => s.Code).ToList();
                    log.LogInformation($"Parsing matches finished, found... {newCodes.Count}");

                    // odstranit neplatne zaznamy pokud vubec jsou
                    context.RemoveRange(dbMatches.Where(w => !w.IsModified));

                    // pridat nove zaznamy, ktere nejsou v puvodnim
                    await context.AddRangeAsync(dbMatches.Where(w => !origCodes.Contains(w.Code)));
                    
                    // updatovat jiz existujici zaznamy, nemusime protoze kolekce je AsTracking()
                    //context.UpdateRange(dbMatches.Where(w => origCodes.Contains(w.Code)));

                    endpoint.State = (int)EState.ToDownload;
                }
            }

            await context.SaveChangesAsync();
            log.LogInformation($"Parsing finished... at: {DateTime.Now}");
        }
    }
}
