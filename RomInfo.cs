using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RetroRoulette
{
    public class RomInfo
    {
        public string name;
        public HashSet<string> regions;
        public HashSet<string> languages;
        public HashSet<string> additionalProperties;

        public RomInfo(string name, IEnumerable<string> regions, IEnumerable<string> languages, IEnumerable<string> additionalProperties)
        {
            this.name = name;
            this.regions = new HashSet<string>(regions);
            this.languages = new HashSet<string>(languages);
            this.additionalProperties = new HashSet<string>(additionalProperties);
        }

        public override string ToString()
        {
            return $"{name} [{String.Join(",", regions)}] [{String.Join(",", languages)}] [{String.Join(",", additionalProperties)}]";
        }

        public string PropsString()
        {
            return String.Join(" | ", new[] { regions, languages, additionalProperties }
                .Where(set => set.Count > 0)
                .Select(set => String.Join(", ", set)));
        }
    }

    static class ROMNameParser
    {
        // Apologies if some of this is wrong, I do not speak all of these languages lol

        static string[] allArticles = new[]
        {
            "The", "A", "An", // English
            "El", "La", "Los", "Las", // Spanish
            "Il", "L'", "La", "I", "Gli", "Le", // Italian (there seem to more than this but it's all I see in the names)
            "Die", "Der", "Das", "Ein", // German (there seem to be way more than this but it's all I see in the names)
            "Le", "Les", "L'", "La", "Un", "Une", "Des", // French
            "As", // Portugese (only one article b/c only one game w/ this article problem at the moment)
            "Het", "De", "Een", // Dutch
        }.Distinct().ToArray();

        public static readonly HashSet<string> VALID_REGIONS = new HashSet<string>
        { 
            // Countries
            "Argentina",
            "Australia",
            "Austria",
            "Belgium",
            "Brazil",
            "Canada",
            "China",
            "Croatia",
            "Czech",
            "Denmark",
            "Finland",
            "France",
            "Germany",
            "Greece",
            "Hong Kong",
            "India",
            "Ireland",
            "Israel",
            "Italy",
            "Japan",
            "Korea",
            "Mexico",
            "Netherlands",
            "New Zealand",
            "Norway",
            "Poland",
            "Portugal",
            "Russia",
            "South Africa",
            "Spain",
            "Sweden",
            "Switzerland",
            "Taiwan",
            "Turkey",
            "UK",
            "USA",
            "United Kingdom",

            // Multi-country areas
            "Scandinavia",
            "Europe",
            "Asia",
            "Latin America",

            // Other
            "World",
            "Unknown"
        };

        public static bool IsBios(string path)
        {
            return Path.GetFileName(path).StartsWith("[BIOS]");
        }

        public static RomInfo Parse(string name)
        {
            Debug.Assert(!IsBios(name));

            string matchGameName = @"(?<gamename>.+?)";
            string matchProps = @"( \((?<props>[^\(\)]+?)\))*";
            // This is only ever [b], we don't care about bad-dump-ness though
            string matchStatus = @"( \[(?<status>.+?)\])?";

            Regex rx = new Regex($"^{matchGameName}{matchProps}{matchStatus}$", RegexOptions.ExplicitCapture);
            Match match = rx.Match(name);

            //If there are things in parenthesis at the end of the name, re-add them (since they got misclassified as properties)
            List<string> maybeProps = GetCapturesForGroup(match, "props");

            List<string>? props = null;
            List<string>? falseProps = null;
            for (int i = 0; i < maybeProps.Count; i++)
            {
                string[] potentialRegions = maybeProps[i].Split(", ");
                if (VALID_REGIONS.IsSupersetOf(potentialRegions))
                {
                    props = maybeProps.Skip(i).ToList();
                    falseProps = maybeProps.Take(i).ToList();
                    break;
                }
            }

            if (props == null || falseProps == null)
            {
                List<string> empty = new List<string>();
                return new RomInfo(FixArticlesInName(name), empty, empty, empty);
                //throw new Exception("No region for game");
            }

            string gameName = GetCaptureForGroup(match, "gamename") + String.Join("", falseProps.Select(p => $" ({p})"));
            gameName = FixArticlesInName(gameName);
            string[] regions = props[0].Split(", ");
            string[] languages = { };

            // TODO: En,Ja,Fr,De,Es,It,Zh-Hant,Zh-Hans,Ko (3ds pokemon moon)

            // Next up is language, which is optional
            // N-games-in-one have N sets of languages, separated by a +
            Regex langRx = new Regex(@"^[A-Z][a-z]([,+][A-Z][a-z])*$");
            IEnumerable<string> remainingProps;
            if (props.Count > 1 && langRx.IsMatch(props[1]))
            {
                languages = props[1].Split(',', '+');
                remainingProps = props.Skip(2);
            }
            else
            {
                remainingProps = props.Skip(1);
            }

            return new RomInfo(gameName, regions, languages, remainingProps);
        }

        static List<string> GetCapturesForGroup(Match match, String groupName)
        {
            return match.Groups.Cast<Group>()
                        .Single(group => group.Name == groupName)
                        .Captures
                        .Cast<Capture>()
                        .Select(capture => capture.Value)
                        .ToList();
        }

        static string? GetCaptureForGroup(Match match, String groupName)
        {
            return GetCapturesForGroup(match, groupName).SingleOrDefault();
        }

        static string FixArticlesInName(string name)
        {
            // NOTE: this doesn't handle
            // Sugoroku, The '92 - Nari Tore - Nariagari Trendy    [should be "The Sugoroku '92"]
            // or War in the Gulf ~ Guerra del Golfo, La

            if (name.Contains(" + "))
            {
                return String.Join(" + ", name.Split(" + ").Select(n => FixArticlesInName(n)));
            }

            string fixedName = name;
            // Now we know we're only working with a single title.
            foreach (string article in allArticles)
            {
                string articleInsert;
                if (article.EndsWith("'")) // L'
                    articleInsert = article;
                else
                    articleInsert = article + " ";

                if (name.EndsWith($", {article}"))
                {
                    fixedName = articleInsert + name.Remove(name.Length - (2 + article.Length));
                    break;
                }

                if (name.Contains($", {article} - "))
                {
                    fixedName = articleInsert + name.Replace($", {article} - ", " - ");
                    break;
                }

                if (name.Contains($", {article} ~ "))
                {
                    fixedName = articleInsert + name.Replace($", {article} ~ ", " ~ ");
                    break;
                }
            }

            return fixedName;
        }
    }
}
