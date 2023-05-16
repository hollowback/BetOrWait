using PuppeteerSharp;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.IO;
using System.Diagnostics;
using System.Reflection.Metadata;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Bow2.FA.Helpers
{
    internal static class Scraper
    {
        internal static async Task<string> ReadAsync(string endpoint)
        {
            IBrowser browser = null;
            try
            {
                //var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
                //{
                //    Path = Path.GetTempPath()
                //});
                var isLocally = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot") != null;
                if (isLocally)
                {
                    // Running locally
                    await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
                }

                Debug.WriteLine($"browser launchasync {(isLocally ? "locally" : "in cloud")}...");
                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    Args = new[] {
                      "--disable-gpu",
                      "--disable-dev-shm-usage",
                      "--disable-setuid-sandbox",
                      "--no-sandbox"}
                    //ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision.ToString()).ExecutablePath
                });

                Debug.WriteLine("browser pagesasync...");
                var page = (await browser.PagesAsync()).First();
                await page.SetCacheEnabledAsync(false);
                Debug.WriteLine("browser goto...");
                await page.GoToAsync(endpoint);
                var wfso = new WaitForSelectorOptions() { Timeout = 3000, Visible = true };
                var button = await page.WaitForSelectorAsync("#onetrust-accept-btn-handler", wfso);
                if (button != null)
                    Debug.WriteLine("button click ...onetrust");
                    await button.ClickAsync(); 

                try
                {
                    Debug.WriteLine("page waitforselector...");
                    var a = await page.WaitForSelectorAsync("a.event__more", wfso);
                    while (a != null)
                    {
                        Debug.WriteLine("page waitforselector...found");
                        //await button.ScrollIntoViewIfNeededAsync();
                        await page.ClickAsync(".event__more");
                        a = await page.WaitForSelectorAsync("a.event__more", wfso);
                    }
                }
                catch (Exception)
                {
                    Debug.WriteLine("page nomorelinks...");
                    // uz neni dalsi link "zobrazit dalsi"
                }

                var element = await page.WaitForSelectorAsync("div.event--results");
                var innerContent = await element.GetPropertyAsync("innerHTML");
                var content = $"<html><body>{await innerContent.JsonValueAsync()}</body></html>";
                return content;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"error: {ex}");
                throw;
            }
            finally
            {
                if (browser != null)
                {
                    Debug.WriteLine($"browser close...");
                    await browser.CloseAsync();
                }
            }
        }
    }
}
