using ImGuiNET;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
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

        // TODO fix names

        [JsonInclude] public bool IncludeHasDisplay = true;
        [JsonInclude] public bool IncludeNoDisplay = false;
        [JsonInclude] public bool IncludeHasSoftwareList = false;
        [JsonInclude] public bool IncludeNoSoftwareList = true;
        [JsonInclude] public bool IncludeImperfectDriver = true;
        [JsonInclude] public bool IncludeGoodDriver = true;
        [JsonInclude] public Dictionary<string, bool> PlayerOneControlTypesInclude = new Dictionary<string, bool>();
        [JsonInclude] public Dictionary<int, bool> PlayerOneButtonCountsInclude = new Dictionary<int, bool>();
        [JsonInclude] public Dictionary<int, bool> PlayerCountsInclude = new Dictionary<int, bool>();
        [JsonInclude] public bool BelieveGuessedYears = true;
        [JsonInclude] public Dictionary<string, bool> YearsInclude = new Dictionary<string, bool>();

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
                YearsInclude = new Dictionary<string, bool>(this.YearsInclude),
            };
        }

        string FixedYear(string year)
        {
            if (BelieveGuessedYears && year.Length == 5 && year[4] == '?')
            {
                return year[0..4];
            }

            return year;
        }

        [Flags]
        public enum Filter
        {
            Base        = 1 << 0,
            ControlType = 1 << 1,
            ButtonCount = 1 << 2,
            PlayerCount = 1 << 3,
            Year        = 1 << 4,

            None = 0,
            All = Base | ControlType | ButtonCount | PlayerCount | Year,
        }

        public IEnumerable<MAMESystemFilterGroup> FilteredGroups(Filter filter)
        {
            IEnumerable<MAMESystemFilterGroup> groups = Program.config.SharedMameConfig.MAMESystemFilterGroups;

            if (filter.HasFlag(Filter.Base))
            {
                groups = groups
                    .Where(g => (IncludeHasDisplay && g.Keys.HasDisplay) || (IncludeNoDisplay && !g.Keys.HasDisplay))
                    .Where(g => (IncludeHasSoftwareList && g.Keys.HasSoftwareList) || (IncludeNoSoftwareList && !g.Keys.HasSoftwareList));

                if (IncludeImperfectDriver && !IncludeGoodDriver)
                {
                    groups = groups.Where(g => g.Keys.DriverStatus == "imperfect");
                }
                else if (IncludeGoodDriver && !IncludeImperfectDriver)
                {
                    groups = groups.Where(g => g.Keys.DriverStatus == "good");
                }
            }

            if (filter.HasFlag(Filter.ControlType))
            {
                groups = groups
                    .Where(g => PlayerOneControlTypesInclude.GetValueOrDefault(g.Keys.PlayerOneControlType, true));
            }

            if (filter.HasFlag(Filter.ButtonCount))
            {
                groups = groups
                    .Where(g => PlayerOneButtonCountsInclude.GetValueOrDefault(g.Keys.PlayerOneButtonCount, true));
            }

            if (filter.HasFlag(Filter.PlayerCount))
            {
                groups = groups
                    .Where(g => PlayerCountsInclude.GetValueOrDefault(g.Keys.Players, true));
            }

            if (filter.HasFlag(Filter.Year))
            {
                groups = groups
                    .Where(g => YearsInclude.GetValueOrDefault(FixedYear(g.Keys.Year), true));
            }

            return groups;
        }

        public IEnumerable<(int, int)> IntListToPairs(IEnumerable<int> intList)
        {
            int start = intList.First();
            int cur = intList.First();

            foreach (int n in intList.Skip(1))
            {
                if (n == cur + 1)
                {
                    cur = n;
                }
                else
                {
                    yield return (start, cur);
                    start = n;
                    cur = n;
                }
            }

            yield return (start, cur);
        }

        public string SummarizeIntList(IEnumerable<int> possibilites, Predicate<int> isEnabled)
        {
            if (!possibilites.Any())
                return "";

            List<(int, int)> pairs = new List<(int, int)>();

            {
                int? start = null;
                int prev = possibilites.First();

                foreach (int n in possibilites)
                {
                    if (start == null && isEnabled(n))
                    {
                        start = n;
                    }
                    else if (start != null && !isEnabled(n))
                    {
                        pairs.Add((start.Value, prev));
                        start = null;
                    }

                    prev = n;
                }

                if (start != null)
                {
                    pairs.Add((start.Value, prev));
                }
            }

            int minVal = possibilites.Min();
            int maxVal = possibilites.Max();

            if (pairs.Count == 1 && pairs[0].Item1 == minVal && pairs[0].Item2 == maxVal)
            {
                return "";
            }
            else
            {
                StringBuilder sb = new StringBuilder();

                foreach ((int min, int max) in pairs)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(" or ");
                    }

                    if (min == max)
                    {
                        sb.Append($"{max}");
                    }
                    else if (min == minVal)
                    {
                        sb.Append($"<={max}");
                    }
                    else if (max == maxVal)
                    {
                        sb.Append($">={min}");
                    }
                    else
                    {
                        sb.Append($"{min}-{max}");
                    }
                }

                return sb.ToString();
            }
        }

        public override void RenderConfigEditor(ref bool edited)
        {
            StringBuilder filtersDesc = new StringBuilder();
            {
                if (IncludeNoDisplay)
                {
                    if (IncludeHasDisplay)
                        filtersDesc.Append("Incl. displayless");
                    else
                        filtersDesc.Append("Only displayless");
                }

                if (IncludeHasSoftwareList)
                {
                    if (filtersDesc.Length > 0)
                        filtersDesc.Append(", ");

                    if (IncludeNoSoftwareList)
                        filtersDesc.Append("Incl. softlist");
                    else
                        filtersDesc.Append("Only softlist");
                }

                if (!(IncludeImperfectDriver && IncludeGoodDriver))
                {
                    if (filtersDesc.Length > 0)
                        filtersDesc.Append(", ");

                    if (IncludeImperfectDriver)
                        filtersDesc.Append("Only \"imperfect\"");
                    else
                        filtersDesc.Append("Only \"good\"");
                }

                {
                    List<string> orderedControlTypes = FilteredGroups(Filter.All & ~Filter.ControlType)
                        .Select(g => g.Keys.PlayerOneControlType).Distinct()
                        .Order()
                        .ToList();

                    List<string> includedControlTypes = orderedControlTypes.Where(c => PlayerOneControlTypesInclude.GetValueOrDefault(c, true)).ToList();

                    if (includedControlTypes.Count != orderedControlTypes.Count)
                    {
                        if (filtersDesc.Length > 0)
                            filtersDesc.Append(" | ");

                        filtersDesc.Append("Only ");
                        filtersDesc.Append(string.Join(", ", includedControlTypes));
                    }
                }

                {
                    List<int> displayedButtonCounts = FilteredGroups(Filter.All & ~Filter.ButtonCount)
                        .Select(g => g.Keys.PlayerOneButtonCount)
                        .Distinct()
                        .Order()
                        .ToList();

                    string countsSummary = SummarizeIntList(displayedButtonCounts, c => PlayerOneButtonCountsInclude.GetValueOrDefault(c, true));

                    if (countsSummary.Length > 0)
                    {
                        if (filtersDesc.Length > 0)
                            filtersDesc.Append(" | ");

                        filtersDesc.Append($"Button count {countsSummary}");
                    }
                }

                {
                    List<int> displayedPlayerCounts = FilteredGroups(Filter.All & ~Filter.PlayerCount)
                        .Select(g => g.Keys.Players)
                        .Distinct()
                        .Order()
                        .ToList();

                    string countsSummary = SummarizeIntList(displayedPlayerCounts, c => PlayerCountsInclude.GetValueOrDefault(c, true));

                    if (countsSummary.Length > 0)
                    {
                        if (filtersDesc.Length > 0)
                            filtersDesc.Append(" | ");

                        filtersDesc.Append($"Player count {countsSummary}");
                    }
                }

                {
                    IEnumerable<string> displayedYears = FilteredGroups(Filter.All & ~Filter.Year)
                        .Select(g => g.Keys.Year)
                        .Distinct()
                        .Select(FixedYear)
                        .Distinct();

                    List<int> displayedKnownYears = displayedYears
                        .Select(year => int.TryParse(year, out int y) ? y : -1 )
                        .Where(y => y != -1)
                        .Order()
                        .ToList();

                    string countsSummary = SummarizeIntList(displayedKnownYears, c => YearsInclude.GetValueOrDefault(c.ToString(), true));

                    if (countsSummary.Length > 0)
                        countsSummary = $"Year {countsSummary}";

                    if (!BelieveGuessedYears)
                    {
                        if (countsSummary.Length > 0)
                            countsSummary += "; ";
                        countsSummary += "Don't believe guessed years";
                    }

                    List<string> uncertainYears = displayedYears
                        .Where(year => !int.TryParse(year, out _))
                        .Distinct()
                        .ToList();

                    List<string> includedUncertainYears = uncertainYears
                        .Where(year => YearsInclude.GetValueOrDefault(year, true))
                        .Order()
                        .ToList();

                    if (includedUncertainYears.Count != uncertainYears.Count)
                    {
                        if (countsSummary.Length > 0)
                            countsSummary += "; ";

                        if (includedUncertainYears.Count == 0)
                        {
                            countsSummary += $"No uncertain years";
                        }
                        else
                        {
                            countsSummary += $"Incl. uncertain {string.Join(", ", includedUncertainYears)}";
                        }
                    }

                    if (countsSummary.Length > 0)
                    {
                        if (filtersDesc.Length > 0)
                            filtersDesc.Append(" | ");

                        filtersDesc.Append(countsSummary);
                    }
                }
            }

            if (!ImGui.CollapsingHeader($"Filter: {filtersDesc.ToString()}###filter"))
                return;

            ImGui.Indent();

            void PairedFilters(string title, Func<FilterKeys, bool> truePredicate, string trueText, ref bool includeTrue, string falseText, ref bool includeFalse, ref bool edited)
            {
                ImGui.TableNextColumn();

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(title);

                ImGui.TableNextColumn();

                {
                    List<MAMESystemInfo> listTrue = Program.config.SharedMameConfig.MAMESystemFilterGroups.Where(g => truePredicate(g.Keys)).SelectMany(g => g.Systems).ToList();

                    if (ImGui.Checkbox($"{trueText} ({listTrue.Count} systems)", ref includeTrue))
                    {
                        edited = true;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(String.Join("\n", listTrue.Select(s => $"{s.NameCore} {s.NameVariantInfo}").Take(30)));
                    }
                }

                ImGui.TableNextColumn();

                {
                    List<MAMESystemInfo> listFalse = Program.config.SharedMameConfig.MAMESystemFilterGroups.Where(g => !truePredicate(g.Keys)).SelectMany(g => g.Systems).ToList();

                    if (ImGui.Checkbox($"{falseText} ({listFalse.Count} systems)", ref includeFalse))
                    {
                        edited = true;
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(String.Join("\n", listFalse.Select(s => $"{s.NameCore} {s.NameVariantInfo}").Take(30)));
                    }
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

            if (ImGui.BeginTable("cols", 4))
            {
                ImGui.TableNextColumn();

                ImGui.TextUnformatted("Player 1 control type:");
                {
                    RenderHistSelector("ctrltypeselect", FilteredGroups(Filter.All & ~Filter.ControlType), s => s.PlayerOneControlType, PlayerOneControlTypesInclude, ref edited);
                }

                ImGui.TableNextColumn();

                ImGui.TextUnformatted("Player 1 button count:");
                {
                    RenderHistSelector("ctrlbtnselect", FilteredGroups(Filter.All & ~Filter.ButtonCount), s => s.PlayerOneButtonCount, PlayerOneButtonCountsInclude, ref edited);
                }

                ImGui.TableNextColumn();

                ImGui.TextUnformatted("Player count:");
                {
                    RenderHistSelector("playercountselect", FilteredGroups(Filter.All & ~Filter.PlayerCount), s => s.Players, PlayerCountsInclude, ref edited);
                }

                ImGui.TableNextColumn();

                ImGui.TextUnformatted("Year:");
                {
                    IEnumerable<MAMESystemFilterGroup> filteredGroups = FilteredGroups(Filter.All & ~Filter.Year);

                    if (filteredGroups.Any())
                    {
                        if (ImGui.Checkbox("(Believe guessed years?)", ref BelieveGuessedYears))
                            edited = true;

                        ImGui.Separator();

                        RenderHistSelector(
                            "yearselect",
                            filteredGroups.Where(g => int.TryParse(FixedYear(g.Keys.Year), out _)),
                            sys => FixedYear(sys.Year),
                            YearsInclude,
                            ref edited
                        );

                        ImGui.Separator();

                        ImGui.AlignTextToFramePadding();
                        ImGui.TextUnformatted("Uncertain years:");

                        IEnumerable<(string YearString, List<MAMESystemInfo> Systems)> uncertainYearsAndSystems = filteredGroups
                            .GroupBy(g => FixedYear(g.Keys.Year))
                            .Where(grp => !int.TryParse(grp.Key, out _))
                            .Select(grp => (YearString: grp.Key, Systems: grp.SelectMany(g => g.Systems).ToList()))
                            .OrderBy(tup => tup.YearString);

                        foreach ((string year, List<MAMESystemInfo> systemsMatch) in uncertainYearsAndSystems)
                        {
                            string strDisplay;

                            if (year == "????")
                            {
                                strDisplay = "Completely unknown";
                            }
                            else if (year.Length == 5)
                            {
                                strDisplay = $"Probably {year[0..4]}";
                            }
                            else
                            {
                                strDisplay = "Unknown " + year.Replace('?', '0') + "-" + year.Replace('?', '9');
                            }

                            strDisplay += $" ({systemsMatch.Count} systems)";

                            bool include = YearsInclude.GetValueOrDefault(year, true);
                            if (ImGui.Checkbox(strDisplay, ref include))
                            {
                                YearsInclude[year] = include;
                                edited = true;
                            }

                            if (ImGui.IsItemHovered())
                            {
                                ImGui.SetTooltip(String.Join("\n", systemsMatch.Take(30).Select(s => $"{s.NameCore} {s.NameVariantInfo}")));
                            }
                        }
                    }
                }

                ImGui.EndTable();
            }

            ImGui.Unindent();
        }

        static void RenderHistSelector<T>(string tableName, IEnumerable<MAMESystemFilterGroup> filteredGroups, Func<FilterKeys, T> fnMap, Dictionary<T, bool> valuesToInclude, ref bool edited)
        {
            if (!filteredGroups.Any())
                return;

            if (ImGui.BeginTable(tableName, 2))
            {
                ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthFixed, 125);

                List<(T Value, List<MAMESystemInfo> Systems)> valueMatchPairs = filteredGroups
                    .Select(g => (Value: fnMap(g.Keys), g.Systems))
                    .GroupBy(t => t.Value)
                    .Select(g => (g.Key, g.SelectMany(gInner => gInner.Systems).ToList()))
                    .OrderBy(t => t.Key)
                    .ToList();

                int maxCount = valueMatchPairs.Max(t => t.Systems.Count);

                foreach ((T value, List<MAMESystemInfo> matches) in valueMatchPairs)
                {
                    ImGui.TableNextColumn();

                    bool include = valuesToInclude.GetValueOrDefault(value, true);
                    if (ImGui.Checkbox($"{value}", ref include))
                    {
                        valuesToInclude[value] = include;
                        edited = true;
                    }

                    ImGui.TableNextColumn();

                    if (!include)
                    {
                        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                    }

                    ImGui.ProgressBar(matches.Count / (float)maxCount, new Vector2(-float.Epsilon, 0), $"{matches.Count}");

                    if (!include)
                    {
                        ImGui.PopStyleColor(3);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(String.Join("\n", matches.Take(30).Select(s => $"{s.NameCore} {s.NameVariantInfo}")));
                    }
                }

                ImGui.EndTable();
            }
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

            this.games = mameNode.FilteredGroups(MAMENodeConfig.Filter.All)
                .SelectMany(g => g.Systems)
                .GroupBy(s => (ParentName: s.CloneOf ?? s.ShortName, s.NameCore))
                .Select(grp => new MAMESystemGroup(grp.Key.NameCore, grp.Key.ParentName, grp, this))
                .ToList();

            if (mameNode.Weight == 0)
            {
                mameNode.Weight = games.Count;
            }
        }
    }

    public record class FilterKeys(bool HasDisplay, bool HasSoftwareList, string DriverStatus, string PlayerOneControlType, int PlayerOneButtonCount, int Players, string Year)
    {
    }

    public record class MAMESystemFilterGroup(FilterKeys Keys, List<MAMESystemInfo> Systems)
    {
    }

    public class SharedMAMEConfig
    {
        private string exePath = "";
        public string EXEPath { get => exePath; set => exePath = value; }

        private List<MAMESystemInfo> mameSystems = new List<MAMESystemInfo>();
        public List<MAMESystemInfo> MAMESystems 
        {
            get => mameSystems;
            set
            {
                mameSystems = value;

                MAMESystemFilterGroups = MAMESystems
                    .GroupBy(s => new FilterKeys(s.HasDisplay, s.HasSoftwareList, s.DriverStatus, s.PlayerOneControlType, s.PlayerOneButtonCount, s.Players, s.Year))
                    .Select(g => new MAMESystemFilterGroup(g.Key, g.ToList()))
                    .ToList();
            }
        }
        public List<MAMESystemFilterGroup> MAMESystemFilterGroups = new List<MAMESystemFilterGroup>();

        private static MAMESystemsBgTask? mameSystemsBgTask = null;

        

        public void RenderEditor()
        {
            ImGui.TextUnformatted("MAME Path: ");
            ImGui.SameLine();

            ImGui.SetNextItemWidth(400);
            ImGui.InputText("##pathedit", ref exePath, 512); // TODO indicate if file doesn't exist

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

    // TODO add some sort of status string

    class MAMESystemsBgTask : BackgroundTask
    {
        private SharedMAMEConfig sharedMameState;

        private string exePath;
        private List<MAMESystemInfo> systems = new List<MAMESystemInfo>();

        public MAMESystemsBgTask(SharedMAMEConfig sharedMameState)
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

            try
            {
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
            }
            catch (System.ComponentModel.Win32Exception) { }

            cts.Token.ThrowIfCancellationRequested();

            ImmutableHashSet<string> playableSets = verifyromsOutput
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("romset ") && !line.EndsWith(" is bad"))
                .Select(line => line.Split())
                .Where(words => words.Length >= 2)
                .Select(words => words[1])
                .ToImmutableHashSet();

            systems = new List<MAMESystemInfo>();

            try
            {
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
            }
            catch (XmlException) { }
        }

        protected override void OnWorkComplete()
        {
            // TODO refresh rootNode when we refresh the list of games
            // TODO maybe don't set if systems empty? Display error?

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
                PlayerOneControlType = "(empty)";
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

        public MAMESystemInfo DefaultSystem => systems.FirstOrDefault(s => s.NameVariantInfo == defaultVariant)!;

        public override void PlayVariant(string variant)
        {
            string shortName = systems.Single(s => s.NameVariantInfo == variant).ShortName;

            Process p = new Process();

            p.StartInfo.FileName = Program.config.SharedMameConfig.EXEPath;
            p.StartInfo.ArgumentList.Add(shortName);

            // MAME looks for paths on the working directory (annoying)
            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(p.StartInfo.FileName);
            p.Start();
        }
    }
}
