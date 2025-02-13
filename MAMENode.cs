using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using static RetroRoulette.AppConfig;
using static System.Net.Mime.MediaTypeNames;

namespace RetroRoulette
{
    public class MAMENodeConfig : GamesNodeConfig
    {
        // Filtering

        [JsonInclude] public bool IncludeHasDisplay = true;
        [JsonInclude] public bool IncludeNoDisplay = false;
        [JsonInclude] public bool IncludeHasSoftwareList = false;
        [JsonInclude] public bool IncludeNoSoftwareList = true;
        [JsonInclude] public bool IncludeImperfectDriver = false;
        [JsonInclude] public bool IncludeGoodDriver = true;
        public Dictionary<string, bool> PlayerOneControlTypesInclude { get; set; } = new Dictionary<string, bool>();
        public Dictionary<int, bool> PlayerOneButtonCountsInclude { get; set; } = new Dictionary<int, bool>();
        public Dictionary<int, bool> PlayerCountsInclude { get; set; } = new Dictionary<int, bool>();
        [JsonInclude] public bool BelieveGuessedYears = true;
        public Dictionary<string, bool> YearsInclude { get; set; } = new Dictionary<string, bool>();


        public override NodeConfig Clone()
        {
            return new MAMENodeConfig
            {
                id = this.id,
                Name = this.Name,
                Enabled = this.Enabled,
                Weight = this.Weight,
                IncludeHasDisplay = this.IncludeHasDisplay,
                IncludeNoDisplay = this.IncludeNoDisplay,
                IncludeHasSoftwareList = this.IncludeHasSoftwareList,
                IncludeNoSoftwareList = this.IncludeNoSoftwareList,
                IncludeImperfectDriver = this.IncludeImperfectDriver,
                IncludeGoodDriver = this.IncludeGoodDriver,
                PlayerOneControlTypesInclude = new Dictionary<string, bool>(this.PlayerOneControlTypesInclude),
                PlayerOneButtonCountsInclude = new Dictionary<int, bool>(this.PlayerOneButtonCountsInclude),
                PlayerCountsInclude = new Dictionary<int, bool>(this.PlayerCountsInclude),
                BelieveGuessedYears = this.BelieveGuessedYears,
                YearsInclude = this.YearsInclude,
            };
        }

        public override void RenderConfigEditor(ref bool edited)
        {
            if (!ImGui.CollapsingHeader("Show filter rules"))
                return;

            ImGui.Indent();

            // TODO can't calculate the filters inline, it means they don't execute if the section is collapsed!

            IEnumerable<MAMESystemInfo> mameSystemInfoFiltered = new List<MAMESystemInfo>(Program.config.SharedMameState.MAMESystems);

            void PairedFilters(string title, Func<MAMESystemInfo, bool> truePredicate, string trueText, ref bool includeTrue, string falseText, ref bool includeFalse, ref bool edited)
            {
                ImGui.TableNextColumn();

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(title);

                ImGui.TableNextColumn();

                {
                    IEnumerable<MAMESystemInfo> enumerableTrue = Program.config.SharedMameState.MAMESystems.Where(s => truePredicate(s));

                    if (ImGui.Checkbox($"{trueText} ({enumerableTrue.Count()} systems)", ref includeTrue))
                    {
                        edited = true;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(String.Join("\n", enumerableTrue.Select(s => $"{s.NameCore} {s.NameVariantInfo}").Take(30)));
                    }
                }

                ImGui.TableNextColumn();

                {
                    IEnumerable<MAMESystemInfo> enumerableFalse = Program.config.SharedMameState.MAMESystems.Where(s => !truePredicate(s));

                    if (ImGui.Checkbox($"{falseText} ({enumerableFalse.Count()} systems)", ref includeFalse))
                    {
                        edited = true;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(String.Join("\n", enumerableFalse.Select(s => $"{s.NameCore} {s.NameVariantInfo}").Take(30)));
                    }
                }

                if (includeTrue && !includeFalse)
                {
                    mameSystemInfoFiltered = mameSystemInfoFiltered.Where(s => truePredicate(s));
                }
                else if (includeFalse && !includeTrue)
                {
                    mameSystemInfoFiltered = mameSystemInfoFiltered.Where(s => !truePredicate(s));
                }
                else if (!includeTrue && !includeFalse)
                {
                    mameSystemInfoFiltered = Enumerable.Empty<MAMESystemInfo>();
                }
            };

            if (ImGui.BeginTable("pairs", 3, ImGuiTableFlags.SizingFixedFit))
            {
                PairedFilters("Display: ", s => s.HasDisplay, "Has display", ref IncludeHasDisplay, "Has no display", ref IncludeNoDisplay, ref edited);
                PairedFilters("Software list: ", s => s.HasSoftwareList, "Has a software list", ref IncludeHasSoftwareList, "Has no software list", ref IncludeNoSoftwareList, ref edited);
                PairedFilters("Emulation status: ", s => s.DriverStatus == "good", "\"good\"", ref IncludeGoodDriver, "\"imperfect\"", ref IncludeImperfectDriver, ref edited);
                ImGui.EndTable();
            }

            ImGui.Dummy(new Vector2(0, 12));

            void RenderHistSelector<T>(string tableName, Func<MAMESystemInfo, T> fnMap, Dictionary<T, bool> valuesToInclude, ref bool edited)
            {
                if (ImGui.BeginTable(tableName, 2))
                {
                    ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthFixed, 125);

                    Dictionary<T, List<MAMESystemInfo>> valuesToMatches = mameSystemInfoFiltered.GroupBy(fnMap).ToDictionary(g => g.Key, g => g.ToList());
                    int maxCount = valuesToMatches.Values.Max(s => s.Count);

                    foreach ((T value, List<MAMESystemInfo> matches) in valuesToMatches.OrderBy(kv => kv.Key))
                    {
                        ImGui.TableNextColumn();

                        bool include = valuesToInclude.GetValueOrDefault(value, true);
                        if (ImGui.Checkbox($"{value}", ref include))
                        {
                            valuesToInclude[value] = include;
                            edited = true;
                        }

                        if (!include)
                        {
                            mameSystemInfoFiltered = mameSystemInfoFiltered.Except(matches);
                        }

                        ImGui.TableNextColumn();

                        ImGui.ProgressBar(matches.Count / (float)maxCount, new Vector2(-float.Epsilon, 0), $"{matches.Count}");

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(String.Join("\n", matches.Take(30).Select(s => $"{s.NameCore} {s.NameVariantInfo}")));
                        }
                    }

                    ImGui.EndTable();
                }
            }

            if (ImGui.CollapsingHeader("Control method") && mameSystemInfoFiltered.Any())
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Player 1 controller type:");
                RenderHistSelector("ctrltypeselect", s => s.PlayerOneControlType, PlayerOneControlTypesInclude, ref edited);

                ImGui.TextUnformatted("Player 1 controller button count:");
                RenderHistSelector("ctrlbtnselect", s => s.PlayerOneButtonCount, PlayerOneButtonCountsInclude, ref edited);
            }

            ImGui.Dummy(new Vector2(0, 12));

            if (ImGui.CollapsingHeader("Player count") && mameSystemInfoFiltered.Any())
            {
                RenderHistSelector("playercountselect", s => s.Players, PlayerCountsInclude, ref edited);
            }

            ImGui.Dummy(new Vector2(0, 12));

            if (ImGui.CollapsingHeader("Year") && mameSystemInfoFiltered.Any())
            {
                if (ImGui.Checkbox("(Believe guessed years?)", ref BelieveGuessedYears))
                    edited = true;

                ImGui.Separator();

                string FixedYear(string year)
                {
                    if (BelieveGuessedYears && year.Length == 5 && year[4] == '?')
                    {
                        return year[0..4];
                    }

                    return year;
                }

                List<(string YearString, List<MAMESystemInfo> Systems)> yearStringsAndSystems = mameSystemInfoFiltered
                    .GroupBy(sys => FixedYear(sys.Year))
                    .Select((grp) => (YearString: grp.Key, Systems: grp.ToList()))
                    .OrderBy(tup => tup.YearString)
                    .ToList();

                Dictionary<int, (List<MAMESystemInfo> Systems, string YearString)> yearIntsToSystems = yearStringsAndSystems
                    .Select(tup => (YearInt: int.TryParse(tup.YearString, out int year) ? year : -1, tup.Systems, tup.YearString))
                    .Where(tup => tup.YearInt != -1)
                    .ToDictionary(t => t.YearInt, t => (t.Systems, t.YearString));

                if (yearIntsToSystems.Any())
                {
                    if (ImGui.BeginTable("yearselect", 2))
                    {
                        ImGui.TableSetupColumn("year", ImGuiTableColumnFlags.WidthFixed, 70);

                        int maxYearCount = yearIntsToSystems.Values.Max(t => t.Systems.Count);

                        int minYear = yearIntsToSystems.Keys.Min();
                        int maxYear = yearIntsToSystems.Keys.Max();

                        for (int year = minYear; year <= maxYear; year++)
                        {
                            if (!yearIntsToSystems.ContainsKey(year))
                                continue;

                            (List<MAMESystemInfo> systemsMatch, string yearString) = yearIntsToSystems[year]; 

                            ImGui.TableNextColumn();

                            bool include = YearsInclude.GetValueOrDefault(yearString, true);
                            if (ImGui.Checkbox($"{year}", ref include))
                            {
                                YearsInclude[yearString] = include;
                                edited = true;
                            }

                            if (!include)
                            {
                                mameSystemInfoFiltered = mameSystemInfoFiltered.Except(systemsMatch);
                            }

                            if (year != maxYear && !yearIntsToSystems.ContainsKey(year + 1))
                                ImGui.Separator();

                            ImGui.TableNextColumn();

                            ImGui.ProgressBar(systemsMatch.Count / (float)maxYearCount, new Vector2(-float.Epsilon, 0), $"{systemsMatch.Count}");

                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip(String.Join("\n", systemsMatch.Select(s => $"{s.NameCore} {s.NameVariantInfo}").Take(30)));
                            }
                        }

                        ImGui.EndTable();
                    }
                }

                ImGui.Separator();

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Uncertain years:");

                foreach ((string yearString, List<MAMESystemInfo> systemsMatch) in yearStringsAndSystems)
                {
                    if (!int.TryParse(yearString, out _))
                    {
                        string strDisplay;

                        if (yearString == "????")
                        {
                            strDisplay = "Completely unknown";
                        }
                        else if (yearString.Length == 5)
                        {
                            strDisplay = $"Probably {yearString[0..4]}";
                        }
                        else
                        {
                            strDisplay = "Unknown " + yearString.Replace('?', '0') + "-" + yearString.Replace('?', '9');
                        }

                        strDisplay += $" ({systemsMatch.Count} systems)";

                        bool include = YearsInclude.GetValueOrDefault(yearString, true);
                        if (ImGui.Checkbox(strDisplay, ref include))
                        {
                            YearsInclude[yearString] = include;
                            edited = true;
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(String.Join("\n", systemsMatch.Select(s => $"{s.NameCore} {s.NameVariantInfo}").Take(30)));
                        }
                    }
                }
            }

            ImGui.Unindent();
        }
    }

    class MAMENode : GamesNode
    {
        public MAMENodeConfig Config => (this.GamesNodeConfig as MAMENodeConfig)!;

        private List<MAMESystemGroup> games;
        public override IEnumerable<Game> Games => games;

        public MAMENode(MAMENodeConfig mameNode)
            : base(mameNode)
        {
            // TODO is this the best way to combine variants?

            this.games = Program.config.SharedMameState.MAMESystems
                .GroupBy(s => (ParentName: s.CloneOf ?? s.ShortName, s.NameCore))
                .Select(grp => new MAMESystemGroup(grp.Key.NameCore, grp.Key.ParentName, grp, this))
                .ToList();

            if (mameNode.Weight == 0)
            {
                mameNode.Weight = games.Count;
            }
        }
    }

    public class SharedMAMEState
    {
        private string exePath = "";
        public string EXEPath { get => exePath; set => exePath = value; }
        public List<MAMESystemInfo> MAMESystems { get; set; } = new List<MAMESystemInfo>();

        private static MAMESystemsBgTask? mameSystemsBgTask = null;

        public void RenderEditor()
        {
            ImGui.TextUnformatted("MAME Path: ");
            ImGui.SameLine();

            ImGui.SetNextItemWidth(400);
            ImGui.InputText("##pathedit", ref exePath, 512);

            ImGui.SameLine();

            ImGui.BeginDisabled(mameSystemsBgTask != null && mameSystemsBgTask.IsRunning);

            if (ImGui.Button("Refresh systems list (will take a bit)"))
            {
                mameSystemsBgTask = new MAMESystemsBgTask(this);
                Program.AddBackgroundTask(mameSystemsBgTask);
            }

            ImGui.EndDisabled();

            ImGui.SameLine();

            if (MAMESystems.Count == 0)
            {
                ImGui.TextUnformatted("No systems in list.");
            }
            else
            {
                ImGui.TextUnformatted($"{MAMESystems.Count} systems in list.");
            }
        }
    }

    class MAMESystemsBgTask : BackgroundTask
    {
        private SharedMAMEState sharedMameState;

        private string exePath;
        private List<MAMESystemInfo> systems = new List<MAMESystemInfo>();

        public MAMESystemsBgTask(SharedMAMEState sharedMameState)
        {
            this.sharedMameState = sharedMameState;
            this.exePath = sharedMameState.EXEPath;
            StartWorkThread();
        }

        protected override void DoWork()
        {
            List<string> verifyromsOutput = new List<string>();
            StringBuilder listxmlOutput = new StringBuilder();

            cts.Token.ThrowIfCancellationRequested();

            using (Process pVerifyroms = new Process())
            {
                using (Process pListxml = new Process())
                {
                    pListxml.StartInfo.FileName = exePath;
                    pListxml.StartInfo.ArgumentList.Add("-listxml");
                    pListxml.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                    pListxml.StartInfo.CreateNoWindow = true;
                    pListxml.StartInfo.UseShellExecute = false;
                    pListxml.StartInfo.RedirectStandardOutput = true;

                    pListxml.OutputDataReceived += (object sendingProcess, DataReceivedEventArgs outLine) => listxmlOutput.AppendLine(outLine.Data);

                    pListxml.Start();
                    pListxml.BeginOutputReadLine();

                    pVerifyroms.StartInfo.FileName = exePath;
                    pVerifyroms.StartInfo.ArgumentList.Add("-verifyroms");
                    pVerifyroms.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                    pVerifyroms.StartInfo.CreateNoWindow = true;
                    pVerifyroms.StartInfo.UseShellExecute = false;
                    pVerifyroms.StartInfo.RedirectStandardOutput = true;

                    pVerifyroms.OutputDataReceived += (_, args) => { if (args.Data != null) verifyromsOutput.Add(args.Data); };

                    pVerifyroms.Start();
                    pVerifyroms.BeginOutputReadLine();

                    // TODO this throw exception... not good?

                    Task.WaitAll(pListxml.WaitForExitAsync(cts.Token), pVerifyroms.WaitForExitAsync(cts.Token));
                }
            }

            cts.Token.ThrowIfCancellationRequested();

            ImmutableHashSet<string> playableSets = verifyromsOutput
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("romset ") && !line.EndsWith(" is bad"))
                .Select(line => line.Split())
                .Where(words => words.Length >= 2)
                .Select(words => words[1])
                .ToImmutableHashSet();

            systems = new List<MAMESystemInfo>();

            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(listxmlOutput.ToString());

            cts.Token.ThrowIfCancellationRequested();

            XmlNodeList mameElems = xmldoc.GetElementsByTagName("mame");

            if (mameElems.Count == 1)
            {
                XmlNode? mameXmlNode = mameElems[0];

                if (mameXmlNode != null)
                {
                    foreach (XmlNode machineNode in mameXmlNode.ChildNodes)
                    {
                        XmlAttributeCollection? attributes = machineNode.Attributes;

                        if (attributes == null)
                            continue;

                        // Ignore anything the user doesn't have the ROMs for.

                        XmlAttribute? nameAttr = attributes["name"];

                        if (nameAttr == null || !playableSets.Contains(nameAttr.Value))
                            continue;

                        // Ignore anything that can't run. Pretty straightforward.
                        // Note: "isdevice" seems to imply runnable="no" so this de facto excludes all devices

                        if (attributes["runnable"]?.Value == "no")
                            continue;

                        // Ignore any drivers which aren't working

                        if (machineNode["driver"]?.Attributes?["status"]?.Value == "preliminary")
                            continue;

                        // After the above filters, there are only like two 0-player games - ignore em!

                        if (machineNode["input"]?.Attributes?["players"]?.Value == "0")
                            continue;

                        // BIOSes are not super exciting and there are also very few of them

                        if (attributes["isbios"]?.Value == "yes")
                            continue;

                        systems.Add(new MAMESystemInfo(machineNode));
                    }
                }
            }

            if (systems.Count == 0)
            {
                // TODO some sort of warning?
            }
        }

        protected override void OnWorkComplete()
        {
            // TODO refresh rootNode when we refresh the list of games

            sharedMameState.MAMESystems = this.systems;
            Program.SaveConfigToDisk();
        }
    }

    public class MAMESystemInfo
    {
        public string ShortName { get; set; } = "";
        public string? CloneOf { get; set; } = null;
        public string NameCore { get; set; } = "";
        public string NameVariantInfo { get; set; } = "";
        public string Year { get; set; } = "????";
        public int Players { get; set; } = 0;
        public string PlayerOneControlType { get; set; } = "";
        public int PlayerOneButtonCount { get; set; } = 0;
        public string DriverStatus { get; set; } = "";
        public bool HasDisplay { get; set; } = false;
        public bool HasSoftwareList { get; set; } = false;

        public MAMESystemInfo() { }

        public MAMESystemInfo(XmlNode machineNode)
        {
            if (machineNode == null)
            {
                throw new ArgumentNullException(nameof(machineNode));
            }

            ShortName = machineNode.Attributes["name"].Value;
            CloneOf = machineNode.Attributes["cloneof"]?.Value;

            string description = machineNode["description"].InnerText;
            int parenIndex = description.IndexOf('(');
            NameCore = parenIndex >= 0 ? description.Substring(0, parenIndex - 1) : description;
            NameVariantInfo = parenIndex >= 0 ? description.Substring(parenIndex) : "";
            Year = machineNode["year"].InnerText;
            Players = int.Parse(machineNode["input"].Attributes["players"].Value);

            if (machineNode["input"].HasChildNodes)
            {
                XmlNode firstPlayerNode = machineNode["input"].FirstChild;
                PlayerOneControlType = firstPlayerNode.Attributes["type"].Value;
                PlayerOneButtonCount = (firstPlayerNode.Attributes["buttons"] != null) ? int.Parse(firstPlayerNode.Attributes["buttons"].Value) : 0;
            }
            else
            {
                PlayerOneControlType = "";
                PlayerOneButtonCount = 0;
            }

            DriverStatus = machineNode["driver"].Attributes["status"].Value;
            HasDisplay = machineNode.SelectSingleNode("display") != null;
            HasSoftwareList = machineNode.SelectSingleNode("softwarelist") != null;

            // Note: I'm not sure if "slot" and "device" info is super usable. It's unclear exactly what it means
            //  and it's hard to come up with a clear heuristic that distinguishes games that should be included from
            //  games that shouldn't
        }
    }

    class MAMESystemGroup : Game
    {
        private readonly string defaultVariant;
        private readonly List<MAMESystemInfo> systems;

        public MAMESystemGroup(string name, string parentShortName, IEnumerable<MAMESystemInfo> systems, Node ownerNode)
            : base(name, ownerNode)
        {
            this.systems = systems.ToList();
            this.defaultVariant = this.systems.FirstOrDefault(s => s.ShortName == parentShortName, this.systems[0]).NameVariantInfo;
        }

        public override bool IsPlayable => true;
        public override IEnumerable<string> Variants() => systems.Select(s => s.NameVariantInfo);
        public override string DefaultVariant() => defaultVariant;

        public override void PlayVariant(string variant)
        {
            string shortName = systems.Single(s => s.NameVariantInfo == variant).ShortName;

            Process p = new Process();

            p.StartInfo.FileName = Program.config.SharedMameState.EXEPath;
            p.StartInfo.ArgumentList.Add(shortName);

            // MAME looks for paths on the working directory (annoying)
            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(p.StartInfo.FileName);
            p.Start();
        }
    }
}
