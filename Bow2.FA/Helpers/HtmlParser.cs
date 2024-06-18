using Bow2.FA.Models;
using Bow2.FA.Structures;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Match = Bow2.FA.Models.Match;

namespace Bow2.FA.Helpers
{
    internal static class HtmlParser
    {
        internal static List<Match> GetMatches(Endpoint endpoint, List<Match> matches, List<Team> teams, League league)
        {
            try
            {
                HtmlDocument doc = new();
                doc.LoadHtml(endpoint.Data);
                var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'event__match')]").Reverse().ToList();
                var lastStamp = new DateTime((int)FindYears(endpoint.Url).Item1, 1, 1);

                foreach (var node in nodes)
                {
                    try
                    {
                        var dbMatch = matches.Find(f => f.Code == node.Id);
                        if (dbMatch == null)
                        {
                            dbMatch = new();
                            matches.Add(dbMatch);
                        }

                        dbMatch.Code = node.Id;

                        // datum
                        dbMatch.Datestamp = FindDate(node, lastStamp);

                        // domaci tym
                        dbMatch.IdTeam1Navigation = FindOrCreateTeam(node, "home", teams, league);

                        // hostujici tym
                        dbMatch.IdTeam2Navigation = FindOrCreateTeam(node, "away", teams, league);

                        // score
                        dbMatch.Pts1Fulltime = FindFullScore(node, "home");
                        dbMatch.Pts11 = FindPartScore(node, "home", 1);
                        dbMatch.Pts2Fulltime = FindFullScore(node, "away");
                        dbMatch.Pts21 = FindPartScore(node, "away", 1);

                        // nazev kola
                        dbMatch.EventRound = FindEventRound(node);
                        // nazev sezony
                        dbMatch.EventHeader = FindEventHeader(node);

                        // liga
                        dbMatch.IdLeagueNavigation = league;

                        // priznak, ze byl zpracovan
                        dbMatch.IsModified = true;

                        lastStamp = dbMatch.Datestamp;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"error: {ex}");
                    }
                }

                return matches;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"error: {ex}");
                throw;
            }
        }

        internal static League GetLeague(string url)
        {
            return new League()
            {
                Name = FindLeague(url),
                Country = FindCountry(url),
                Sport = FindSport(url)
            };
        }

        private static Team FindOrCreateTeam(HtmlNode node, string locationPattern, List<Team> dbTeams, League league)
        {
            var teamCode = FindTeamCode(node, locationPattern);
            var team = dbTeams.Find(f => f.Code == teamCode);
            if (team == null)
            {
                team = new Team()
                {
                    Code = FindTeamCode(node, locationPattern),
                };
                dbTeams.Add(team);
            }
            team.Name = FindTeamName(node, locationPattern);
            team.Country = league.Country;
            team.Sport = league.Sport;

            return team;
        }

        private static DateTime FindDate(HtmlNode node, DateTime lastStamp)
        {
            var formattedInput = node.ChildNodes.First(f => f.HasClass("event__time")).InnerText + " " + lastStamp.Year.ToString();
            if (!DateTime.TryParseExact(formattedInput, "d.M. H:mm yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                throw new MissingXPathException("missing event time");

            if (date < lastStamp)
            {
                // pridame rok
                date = date.AddYears(1);
            }

            return date;
        }

        private static string FindTeamCode(HtmlNode node, string locationPattern)
        {
            var htmlString = node.ChildNodes.First(f => f.HasClass($"event__{locationPattern}Participant")).OuterHtml;
            string pattern = @"src=""([^""]+)\/([^\/]+)\.png""";
            var mtch = Regex.Match(htmlString, pattern);
            if (!mtch.Success)
                throw new MissingXPathException($"missing {locationPattern} team id");
            return mtch.Groups[2].Value;
        }

        private static string FindTeamName(HtmlNode node, string locationPattern)
        {
            return node.ChildNodes.First(f => f.HasClass($"event__{locationPattern}Participant")).InnerText;
        }

        private static short? FindFullScore(HtmlNode node, string locationPattern)
        {
            if (!short.TryParse(node.ChildNodes.First(f => f.HasClass($"event__score--{locationPattern}")).InnerText, out short score))
                return null;
            return score;
        }

        private static short? FindPartScore(HtmlNode node, string locationPattern, short partNum)
        {
            var part = node.ChildNodes.FirstOrDefault(f => f.HasClass($"event__part--{locationPattern}") && f.HasClass($"event__part--{partNum}"))?.InnerText;
            if (!ExtractPartScore(part, out short score))
                return null;
            return score;
        }

        private static bool ExtractPartScore(string input, out short score)
        {
            score = 0;
            if (string.IsNullOrEmpty(input))
                return false;
            string pattern = @"\((\d+)\)";
            var match = Regex.Match(input, pattern);

            if (match.Success)
            {
                string intString = match.Groups[1].Value;
                if (short.TryParse(intString, out score))
                {
                    return true;
                }
            }
            return false;
        }

        private static short FindSport(string url)
        {
            return Converter.ConvertEnumToShort<ESport>(url.Split('/', StringSplitOptions.RemoveEmptyEntries)[2]);
        }

        private static string FindCountry(string url)
        {
            return url.Split('/', StringSplitOptions.RemoveEmptyEntries)[3];
        }

        private static string FindLeague(string url)
        {
            return url.Split('/', StringSplitOptions.RemoveEmptyEntries)[4];
        }

        private static (short?, short?) FindYears(string url)
        {
            var leagueWithYears = url.Split('/', StringSplitOptions.RemoveEmptyEntries)[4];
            string pattern = @"(\d{4})-(\d{4})$"; // Matches "YYYY-YYYY" at the end of the string
            var match = Regex.Match(leagueWithYears, pattern);

            if (match.Success)
            {
                var startYear = short.Parse(match.Groups[1].Value);
                var endYear = short.Parse(match.Groups[2].Value);
                return (startYear, endYear);
            }

            // Return default values if no match found
            return (null, null);
        }

        private static string FindEventRound(HtmlNode node)
        {
            var round = FindNearestSiblingByClassName(node, "event__round");
            return round.InnerText;
        }

        private static string FindEventHeader(HtmlNode node)
        {
            var header = FindNearestSiblingByClassName(node, "wclLeagueHeader");
            return header.Descendants().First(w => w.HasClass("event__titleInfo")).InnerText;
        }

        private static HtmlNode FindNearestSiblingByClassName(HtmlNode node, string className)
        {
            while (node != null && !node.HasClass(className))
            {
                node = node.PreviousSibling;
            }

            return node;
        }
    }
}
