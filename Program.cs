using ImGuiNET;
using SharpGen.Runtime.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Veldrid;
using Veldrid.MetalBindings;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Vulkan.Xlib;
using static RetroRoulette.SavedConfig;

// Ideas for stuff to add support for:
//  - MAME arcade games
//  - Bluemaxima Flashpoint? Open up the SQLite databse in Data/. Maybe use the main curated list as an optional filter?
//  - itch.io games?

// Feature/improvement ideas:
// - multithread startup
// - list roms that weren't playable
// - option to hide names but not systems (in reels)
// - filters for additional flags on ROMs, and regions. E.g. only play unlicensed games, or only play Korean games
// - maybe a way to tag folder as being type X of a type group. e.g. could tag as "popular" or "obscure" and have that as a separate weight adjustment
//   - "Axes"
// - some way of creating custom tags/collections? would be cool to do like a "Classics of Game" night
// - support using no-intro, redump, etc xmls to get more info about ROMs?
// - use xmls to group games with different names? maybe even group games across multiple systems?

namespace RetroRoulette
{
    class SavedConfig
    {
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
        [JsonDerivedType(typeof(FileFolderNode), "FileFolder")]
        [JsonDerivedType(typeof(GroupNode), "Group")]
        [JsonDerivedType(typeof(NameListNode), "NameList")]
        [JsonDerivedType(typeof(MAMENode), "MAME")]
        public class Node 
        {
            public int id = 0;
            private string name = "";

            [JsonPropertyOrder(-2)]
            public string Name { get => name; set => name = value; }

            [JsonIgnore]
            public ref string NameEditable => ref name;
        }

        public class GroupNode : Node
        {
            public List<Node> ChildNodes { get; set; } = new List<Node>();
        }

        public abstract class GamesNode : Node
        {
            [JsonPropertyOrder(-1)]
            public bool Enabled { get; set; } = true;
            [JsonPropertyOrder(-1)]
            public double Weight { get; set; } = 0;

            public abstract void RenderConfigEditor(ref bool edited);
        }

        public class FileFolderNode : GamesNode
        {
            private string dirPath = "";
            public string DirPath { get => dirPath; set => dirPath = value; }
            public List<string> PlayCommand { get; set; } = new List<string>();
            public List<string> SupportedExtensions { get; set; } = new List<string>();

            public override void RenderConfigEditor(ref bool edited)
            {
                ImGui.SetNextItemWidth(400);
                ImGui.InputText("##pathedit", ref dirPath, 512);

                if (ImGui.IsItemEdited())
                    edited = true;

                // TODO

                //if (!Directory.Exists(dirPathEdit))
                //{
                //    ImGui.Text("oh no!");
                //}

                ImGui.SameLine();

                ImGui.SetNextItemWidth(120);

                if (ImGui.BeginCombo("##extensions", (SupportedExtensions.Count == 0) ? "(no filetypes selected)" : String.Join(", ", SupportedExtensions), ImGuiComboFlags.NoArrowButton))
                {
                    foreach (string fileType in Program.GetFileTypesInDir(DirPath))
                    {
                        bool included = SupportedExtensions.Contains(fileType);

                        if (ImGui.Checkbox(fileType, ref included))
                        {
                            if (included)
                            {
                                SupportedExtensions.Add(fileType);
                            }
                            else
                            {
                                SupportedExtensions.Remove(fileType);
                            }
                            edited = true;
                        }
                    }

                    ImGui.EndCombo();
                }

                ImGui.SameLine();

                ImGui.TextUnformatted("->");

                ImGui.SameLine();

                ImGui.SetNextItemWidth(120);
                if (ImGui.BeginCombo("##action", (PlayCommand.Count == 0) ? "(no play action)" : "Launch Program:", ImGuiComboFlags.NoArrowButton))
                {
                    if (ImGui.Selectable("None"))
                    {
                        PlayCommand.Clear();
                        edited = true;
                    }

                    if (ImGui.Selectable("Launch program..."))
                    {
                        PlayCommand = new List<string> { "" };
                        edited = true;
                    }

                    ImGui.EndCombo();
                }

                if (PlayCommand.Count > 0)
                {
                    ImGui.SameLine();

                    for (int i = 0; i < PlayCommand.Count; i++)
                    {
                        ImGui.SameLine();

                        string cmdComponent = PlayCommand[i];

                        ImGui.SetNextItemWidth(Math.Max(20, ImGui.CalcTextSize(cmdComponent).X + ImGui.GetStyle().FramePadding.X * 2));
                        ImGui.InputText($"##programname-{i}", ref cmdComponent, 1024, ImGuiInputTextFlags.NoHorizontalScroll);

                        if (ImGui.IsItemEdited())
                        {
                            PlayCommand[i] = cmdComponent;
                            edited = true;
                        }
                    }

                    ImGui.SameLine();

                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("<path to file>");

                    ImGui.SameLine();

                    ImGui.BeginDisabled(PlayCommand.Count == 1);
                    if (ImGui.Button("-"))
                    {
                        PlayCommand.RemoveAt(PlayCommand.Count - 1);
                        edited = true;
                    }
                    ImGui.EndDisabled();

                    ImGui.SameLine();

                    if (ImGui.Button("+"))
                    {
                        PlayCommand.Add("");
                        edited = true;
                    }
                }
            }
        }

        public class MAMENode : GamesNode
        {
            private string exePath = "";
            public string ExePath { get => exePath; set => exePath = value; }

            public override void RenderConfigEditor(ref bool edited)
            {
                ImGui.SetNextItemWidth(400);
                ImGui.InputText("##pathedit", ref exePath, 512);

                if (ImGui.IsItemEdited())
                    edited = true;
            }
        }

        public class NameListNode : GamesNode
        {
            public List<string> NameList { get; set; } = new List<string>();
            public List<string> PlayCommand { get; set; } = new List<string>();

            public override void RenderConfigEditor(ref bool edited)
            {
                ImGui.SetNextItemWidth(150);
                if (ImGui.BeginCombo("##aa", $"{NameList.Count} name{(NameList.Count == 1 ? "" : "s")}...###stringlistheader", ImGuiComboFlags.HeightLargest))
                {
                    string namesList = String.Join('\n', NameList);

                    // TODO when user hits ctrl+v, increase string size by size of clipboard

                    ImGui.InputTextMultiline("##names", ref namesList, (uint)namesList.Length + 262144, new Vector2(-float.Epsilon, 200));

                    if (ImGui.IsItemEdited())
                    {
                        NameList = namesList.Split('\n').Select(s => s.Trim()).Where(s => !String.IsNullOrEmpty(s)).ToList();
                        edited = true;
                    }

                    ImGui.EndCombo();
                }

                ImGui.SameLine();

                ImGui.TextUnformatted("->");

                ImGui.SameLine();

                ImGui.SetNextItemWidth(120);
                if (ImGui.BeginCombo("##action", (PlayCommand.Count == 0) ? "(no play action)" : "Launch Program:", ImGuiComboFlags.NoArrowButton))
                {
                    if (ImGui.Selectable("None"))
                    {
                        PlayCommand.Clear();
                        edited = true;
                    }

                    if (ImGui.Selectable("Launch program..."))
                    {
                        PlayCommand = new List<string> { "" };
                        edited = true;
                    }

                    ImGui.EndCombo();
                }

                if (PlayCommand.Count > 0)
                {
                    ImGui.SameLine();

                    string programName = PlayCommand[0];
                    ImGui.SetNextItemWidth(-float.Epsilon);
                    ImGui.InputText("##programname", ref programName, 1024);

                    if (ImGui.IsItemEdited())
                    {
                        PlayCommand[0] = programName;
                        edited = true;
                    }
                }
            }
        }

        public GroupNode RootNode { get; set; } = new GroupNode();

        /////////////////////////////////////////////////////////////////////////

        public void InitIds()
        {
            int nextId = 1;
            InitIdsInternal(RootNode, ref nextId);
        }

        private static void InitIdsInternal(Node node, ref int nextId)
        {
            Debug.Assert(node.id == 0);
            node.id = nextId++;

            if (node is GroupNode groupNode)
            {
                foreach (Node childNode in groupNode.ChildNodes)
                {
                    InitIdsInternal(childNode, ref nextId);
                }
            }
        }
    }

    abstract class Game
    {
        public string name;
        public Node ownerNode;

        public Game(string name, Node ownerNode)
        {
            this.name = name;
            this.ownerNode = ownerNode;
        }

        public abstract IEnumerable<string> Variants();
        public abstract string DefaultVariant();
        public abstract bool IsPlayable { get; }
        public abstract void PlayVariant(string variant);

        public bool MatchesNameFilter(string nameFilter)
        {
            // TODO match against a version with no punctuation?

            return nameFilter.Length == 0 || name.Contains(nameFilter, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    class SimpleGame : Game
    {
        private string[] playCommand;

        public SimpleGame(string name, Node ownerNode, string[] playCommand)
            : base(name, ownerNode)
        {
            this.playCommand = playCommand;
        }

        public override string DefaultVariant() => "";
        public override bool IsPlayable => playCommand.Length > 0;
        public override void PlayVariant(string variant)
        {
            Debug.Assert(IsPlayable);

            Process p = new Process();

            p.StartInfo.FileName = playCommand[0];
            foreach (string arg in playCommand.Skip(1))
                p.StartInfo.ArgumentList.Add(arg);

            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(playCommand[0]);
            p.Start();
        }

        public override IEnumerable<string> Variants() => new[] { "" };
    }

    class ROM
    {
        public string path;
        public RomInfo details;

        public ROM(string path)
        {
            this.path = path;
            details = ROMNameParser.Parse(Path.GetFileNameWithoutExtension(this.path));
        }
    }

    class ROMSet : Game
    {
        protected IEnumerable<ROM> roms;

        public ROMSet(string name, FileFolderNode gamesNode, IEnumerable<ROM> roms)
            : base(name, gamesNode)
        {
            this.roms = roms;
        }

        public override IEnumerable<string> Variants() => roms.Select(rom => rom.details.PropsString());
        public override string DefaultVariant() => DefaultROM().details.PropsString();
        public override bool IsPlayable => (ownerNode as FileFolderNode).PlayCommand.Count() > 0;
        public override void PlayVariant(string variant)
        {
            ROM rom = roms.First(rom => rom.details.PropsString() == variant);

            string[] command = (ownerNode as FileFolderNode).PlayCommand.Append(rom.path).ToArray();

            Process p = new Process();

            p.StartInfo.FileName = command[0];
            foreach (string arg in command.Skip(1))
                p.StartInfo.ArgumentList.Add(arg);

            // MAME looks for paths on the working directory (annoying)
            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(command[0]);
            p.Start();
        }

        protected ROM DefaultROM()
        {
            List<ROM> filteredRoms = new List<ROM>(roms);

            List<ROM> usaRoms = filteredRoms.Where(rom => rom.details.regions.Contains("USA")).ToList();
            if (usaRoms.Count > 0)
            {
                filteredRoms = usaRoms;
            }

            List<ROM> noPropsRoms = filteredRoms.Where(rom => rom.details.additionalProperties.Count == 0).ToList();
            if (noPropsRoms.Count > 0)
            {
                filteredRoms = noPropsRoms;
            }

            return filteredRoms.First();
        }
    }

    abstract class Node
    {
        public readonly int id;
        public readonly string name;

        public abstract bool Enabled { get; set; }
        public abstract double Weight { get; set; }
        public abstract double EffectiveWeight { get; }

        public abstract IEnumerable<Game> Games { get; }
        public IEnumerable<Game> FilteredGames => Games.Where(game => game.MatchesNameFilter(Program.gameNameFilter));
        public IEnumerable<Game> FilteredEnabledGames => Games.Where(game => game.ownerNode.Enabled && game.MatchesNameFilter(Program.gameNameFilter));
        public bool AnyGamesMatchFilter => Games.Any(game => game.MatchesNameFilter(Program.gameNameFilter));

        public Node(int id, string name)
        {
            this.id = id;
            this.name = name;
        }

        public abstract SavedConfig.Node ToSavedConfigNode();
    }

    abstract class GamesNode : Node
    {
        public override bool Enabled { get; set; }
        public override double Weight { get; set; }
        public override double EffectiveWeight => (Enabled && AnyGamesMatchFilter) ? Weight : 0;

        public GamesNode(SavedConfig.GamesNode savedConfigGamesNode)
            : base(savedConfigGamesNode.id, savedConfigGamesNode.Name)
        {
            Enabled = savedConfigGamesNode.Enabled;
            Weight = savedConfigGamesNode.Weight;
        }

        public void ResetWeight()
        {
            Weight = Games.Count();
        }
    }

    class NameListNode : GamesNode
    {
        private readonly List<SimpleGame> games;
        private readonly List<string> playCommand;

        public override IEnumerable<Game> Games => games;

        public NameListNode(SavedConfig.NameListNode savedConfigNameListNode)
            : base(savedConfigNameListNode)
        {
            playCommand = new List<string>(savedConfigNameListNode.PlayCommand);
            games = savedConfigNameListNode.NameList.Select(s => new SimpleGame(s, this, playCommand.ToArray())).ToList();

            if (Weight == 0)
            {
                Weight = games.Count;
            }
        }

        public override SavedConfig.Node ToSavedConfigNode()
        {
            return new SavedConfig.NameListNode()
            {
                id = id,
                Name = name,
                Enabled = Enabled,
                Weight = Weight,
                NameList = games.Select(g => g.name).ToList(),
                PlayCommand = new List<string>(playCommand),
            };
        }
    }

    class MAMESystemInfo
    {
        public string ShortName { get; set; }
        public string? CloneOf { get; set; }
        public string Name { get; set; }
        public string NameCore { get; set; }
        public string NameVariantInfo { get; set; }
        public string Year { get; set; }
        private int normalizedYear;
        public int NormalizedYear { get => normalizedYear; set => normalizedYear = value; }
        public int Players { get; set; }
        public string PlayerOneControlType { get; set; }
        public int PlayerOneButtonCount { get; set; }
        public string DriverStatus { get; set; }
        public bool HasDisplay { get; set; }
        public bool HasSoftwareList { get; set; }

        public MAMESystemInfo(XmlNode machineNode)
        {
            if (machineNode == null)
            {
                throw new ArgumentNullException(nameof(machineNode));
            }

            ShortName = machineNode.Attributes["name"].Value;
            CloneOf = machineNode.Attributes["cloneof"]?.Value;

            Name = machineNode["description"].InnerText;
            int parenIndex = Name.IndexOf('(');
            NameCore = parenIndex >= 0 ? Name.Substring(0, parenIndex - 1) : Name;
            NameVariantInfo = parenIndex >= 0 ? Name.Substring(parenIndex) : "";

            Year = machineNode["year"].InnerText;

            if (!int.TryParse(Year, out normalizedYear))
            {
                if (Year == "19??")
                {
                    normalizedYear = 1980; // Lean early since unknown
                }
                else if (Year == "20??")
                {
                    normalizedYear = 2000; // Lean early since unknown
                }
                else if (Year == "????")
                {
                    normalizedYear = 1980; // Lean early since unknown
                }
                else if (Year[3] == '?')
                {
                    normalizedYear = int.Parse(Year.Substring(0, 3) + "5");
                }
                else if (Year.Length == 5 && Year.EndsWith("?"))
                {
                    normalizedYear = int.Parse(Year.Substring(0, 4));
                }
            }

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

            p.StartInfo.FileName = (ownerNode as MAMENode).exePath;
            p.StartInfo.ArgumentList.Add(shortName);

            // MAME looks for paths on the working directory (annoying)
            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(p.StartInfo.FileName);
            p.Start();
        }
    }

    class MAMENode : GamesNode
    {
        public readonly string exePath;
        private List<MAMESystemGroup> games;

        public override IEnumerable<Game> Games => games;

        public MAMENode(SavedConfig.MAMENode mameNode)
            : base(mameNode)
        {
            // How to combine variants of games without ignoring interesting bootlegs? maybe base it on manufacturer?

            // TODO refresh this when we refresh the list of games

            // TODO filtering/weighting for this node?

            this.exePath = mameNode.ExePath;
            this.games = new List<MAMESystemGroup>();
            Program.AddBackgroundTask(new MAMESystemsBgTask(this));
        }

        class MAMESystemsBgTask : BackgroundTask
        {
            private MAMENode mameNode;
            private string exePath;
            private List<MAMESystemGroup> games = new List<MAMESystemGroup>();

            public MAMESystemsBgTask(MAMENode mameNode)
            {
                this.mameNode = mameNode;
                this.exePath = mameNode.exePath;
                StartWorkThread();
            }

            protected override void DoWork()
            {
                string verifyromsOutput;

                if (File.Exists("verifyroms.txt"))
                {
                    verifyromsOutput = File.ReadAllText("verifyroms.txt");
                }
                else
                {
                    using (Process pVerifyroms = new Process())
                    {
                        pVerifyroms.StartInfo.FileName = exePath;
                        pVerifyroms.StartInfo.ArgumentList.Add("-verifyroms");
                        pVerifyroms.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                        pVerifyroms.StartInfo.CreateNoWindow = true;
                        pVerifyroms.StartInfo.UseShellExecute = false;
                        pVerifyroms.StartInfo.RedirectStandardOutput = true;

                        pVerifyroms.Start();
                        verifyromsOutput = pVerifyroms.StandardOutput.ReadToEnd();
                        pVerifyroms.WaitForExit();
                    }

                    File.WriteAllText("verifyroms.txt", verifyromsOutput);
                }

                ImmutableHashSet<string> playableSets = verifyromsOutput
                    .Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("romset ") && line.EndsWith(" is good")) // TODO should we ever include "is best available"? distinguish between needs redump and missing rom?
                    .Select(line => line.Split())
                    .Where(words => words.Length >= 2)
                    .Select(words => words[1])
                    .ToImmutableHashSet();

                XmlDocument xmldoc = new XmlDocument();

                if (File.Exists("listxml.txt"))
                {
                    xmldoc.Load("listxml.txt");
                }
                else
                {
                    using (Process pListxml = new Process())
                    {
                        pListxml.StartInfo.FileName = exePath;
                        pListxml.StartInfo.ArgumentList.Add("-listxml");
                        pListxml.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
                        pListxml.StartInfo.CreateNoWindow = true;
                        pListxml.StartInfo.UseShellExecute = false;
                        pListxml.StartInfo.RedirectStandardOutput = true;

                        pListxml.Start();

                        string xml = pListxml.StandardOutput.ReadToEnd();
                        File.WriteAllText("listxml.txt", xml);
                        xmldoc.LoadXml(xml);

                        pListxml.WaitForExit();
                    }
                }

                XmlNode mameXmlNode = xmldoc.GetElementsByTagName("mame")[0];

                List<MAMESystemInfo> systems = new List<MAMESystemInfo>();

                foreach (XmlNode machineNode in mameXmlNode.ChildNodes)
                {
                    // Ignore anything the user doesn't have the ROMs for.

                    string? name = machineNode.Attributes?["name"]?.Value;

                    if (name == null || !playableSets.Contains(name))
                        continue;

                    // Ignore anything that can't run. Pretty straightforward.
                    // Note: "isdevice" seems to imply runnable="no" so this de facto excludes all devices

                    if (machineNode.Attributes?["runnable"]?.Value == "no")
                        continue;

                    // Ignore any drivers which aren't working

                    if (machineNode["driver"]?.Attributes?["status"]?.Value == "preliminary")
                        continue;

                    // After the above filters, there are only like two 0-player games - ignore em!

                    if (machineNode["input"]?.Attributes?["players"]?.Value == "0")
                        continue;

                    // BIOSes are not super exciting and there are also very few of them

                    if (machineNode.Attributes?["isbios"]?.Value == "yes")
                        continue;

                    systems.Add(new MAMESystemInfo(machineNode));
                }

                games = systems.GroupBy(s => (ParentName: s.CloneOf ?? s.ShortName, s.NameCore)).Select(grp => new MAMESystemGroup(grp.Key.NameCore, grp.Key.ParentName, grp, mameNode)).ToList();
            }

            protected override void OnWorkComplete()
            {
                mameNode.games = this.games;

                if (mameNode.Weight == 0)
                {
                    mameNode.Weight = games.Count;
                }
            }
        }

        public override SavedConfig.Node ToSavedConfigNode()
        {
            return new SavedConfig.MAMENode()
            {
                id = id,
                Name = name,
                Enabled = Enabled,
                Weight = Weight,
                ExePath = exePath,
            };
        }
    }

    class FileFolderNode : GamesNode
    {
        private readonly string dirPath;
        private readonly List<string> playCommand;
        public IEnumerable<string> PlayCommand => playCommand;
        private readonly List<string> supportedExtensions;

        private List<ROMSet> romsets;

        public override IEnumerable<Game> Games => romsets;

        public FileFolderNode(SavedConfig.FileFolderNode savedConfigFileFolderNode)
            : base(savedConfigFileFolderNode)
        {
            dirPath = savedConfigFileFolderNode.DirPath;
            playCommand = new List<string>(savedConfigFileFolderNode.PlayCommand);
            supportedExtensions = new List<string>(savedConfigFileFolderNode.SupportedExtensions);

            romsets = new List<ROMSet>();
            Program.AddBackgroundTask(new RomsetsBgTask(this));
        }

        class RomsetsBgTask : BackgroundTask
        {
            private FileFolderNode fileFolderNode;
            private List<ROMSet> romsets;

            public RomsetsBgTask(FileFolderNode fileFolderNode)
            {
                this.fileFolderNode = fileFolderNode;
                this.romsets = new List<ROMSet>();
                StartWorkThread();
            }

            protected override void DoWork()
            {
                if (Directory.Exists(fileFolderNode.dirPath))
                {
                    romsets = Directory.EnumerateFiles(fileFolderNode.dirPath, "*", SearchOption.AllDirectories)
                    .Where(filePath => fileFolderNode.supportedExtensions.Contains(Path.GetExtension(filePath)))
                    .Where(filePath => !ROMNameParser.IsBios(filePath)) // TODO
                    .Select(filePath => new ROM(filePath))
                    .GroupBy(rom => rom.details.name)
                    .Select(grouping => new ROMSet(grouping.Key, fileFolderNode, grouping))
                    .ToList();
                }
            }

            protected override void OnWorkComplete()
            {
                fileFolderNode.romsets = romsets;

                if (fileFolderNode.Weight == 0)
                {
                    fileFolderNode.Weight = romsets.Count;
                }
            }
        }

        public override SavedConfig.Node ToSavedConfigNode()
        {
            return new SavedConfig.FileFolderNode()
            {
                id = id,
                Name = name,
                Enabled = Enabled,
                Weight = Weight,
                DirPath = dirPath,
                PlayCommand = new List<string>(playCommand),
                SupportedExtensions = new List<string>(supportedExtensions),
            };
        }
    }

    class GroupNode : Node
    {
        public List<Node> subNodes;

        public override bool Enabled
        {
            get => subNodes.Any(subNode => subNode.Enabled);
            set => subNodes.ForEach(subNode => subNode.Enabled = value);
        }

        public override double Weight
        {
            get => subNodes.Sum(subNode => subNode.Weight);
            set
            {
                double mulFactor = value / Weight;
                subNodes.ForEach(subNode => subNode.Weight *= mulFactor);
            }
        }

        public override double EffectiveWeight => Enabled ? subNodes.Sum(subNode => subNode.EffectiveWeight) : 0;

        public override IEnumerable<Game> Games => subNodes.SelectMany(subNode => subNode.Games);

        public GroupNode(SavedConfig.GroupNode savedConfigGroupNode)
            : base(savedConfigGroupNode.id, savedConfigGroupNode.Name)
        {
            subNodes = new List<Node>();

            foreach (SavedConfig.Node childNode in savedConfigGroupNode.ChildNodes)
            {
                if (childNode is SavedConfig.GroupNode childGroupNode)
                {
                    subNodes.Add(new GroupNode(childGroupNode));
                }
                else if (childNode is SavedConfig.FileFolderNode childFileFolderNode)
                {
                    subNodes.Add(new FileFolderNode(childFileFolderNode));
                }
                else if (childNode is SavedConfig.NameListNode childNameListNode)
                {
                    subNodes.Add(new NameListNode(childNameListNode));
                }
                else if (childNode is SavedConfig.MAMENode childMameNode)
                {
                    subNodes.Add(new MAMENode(childMameNode));
                }
            }
        }

        public override SavedConfig.Node ToSavedConfigNode()
        {
            return new SavedConfig.GroupNode()
            {
                id = id,
                Name = name,
                ChildNodes = subNodes.Select(subNode => subNode.ToSavedConfigNode()).ToList()
            };
        }

        public IEnumerable<Node> AllSubNodes()
        {
            foreach (Node subNode in subNodes)
            {
                yield return subNode;

                if (subNode is GroupNode groupSubNode)
                {
                    foreach (Node subNodeChild in groupSubNode.AllSubNodes())
                    {
                        yield return subNodeChild;
                    }
                }
            }
        }

        public Game? WeightedFilteredRandomGame()
        {
            double next = Random.Shared.NextDouble() * EffectiveWeight;
            double total = 0;

            foreach (Node node in subNodes)
            {
                total += node.EffectiveWeight;

                if (next < total)
                {
                    if (node is GamesNode gamesNode)
                    {
                        List<Game> games = gamesNode.FilteredGames.ToList();
                        return games[Random.Shared.Next(games.Count)];
                    }
                    else if (node is GroupNode groupNode)
                    {
                        return groupNode.WeightedFilteredRandomGame();
                    }
                }
            }

            return null;
        }
    }

    class Reel
    {
        public bool spinning;
        private Game? game;
        public string? variant;

        public Game? Game
        {
            get { return game; }
            set { game = value; variant = (game == null) ? null : game.DefaultVariant(); }
        }

        public Reel(Game? game)
        {
            this.spinning = true;
            this.Game = game;
        }
    }

    abstract class BackgroundTask
    {
        private readonly Thread thread;

        public bool IsRunning => thread.IsAlive;
        
        protected BackgroundTask()
        {
            this.thread = new Thread(DoWork);
        }

        protected void StartWorkThread() => thread.Start();

        protected abstract void DoWork();

        public bool TryFinish()
        {
            if (!thread.IsAlive)
            {
                OnWorkComplete();
                return true;
            }

            return false;
        }

        protected abstract void OnWorkComplete();
    }

    class Program
    {
        static ImFontPtr font20;
        static ImFontPtr font30;
        static ImFontPtr font40;

        static bool renderImguiDemo = false;

        // Reels

        static int nReels = 3;
        static List<Reel> reels = new List<Reel>();
        static Stopwatch appStopwatch = Stopwatch.StartNew();
        const double spinTickTime = 0.04f;
        static double nextSpinTick = 0;

        // Config

        static GroupNode rootNode;
        public static string gameNameFilter = "";
        static Node? nodeDraggedProgressBar = null;

        public static List<BackgroundTask> backgroundTasks = new List<BackgroundTask>();

        public static void AddBackgroundTask(BackgroundTask task)
        { 
            backgroundTasks.Add(task); 
        }

        static void LoadConfigFromDisk()
        {
            SavedConfig? savedConfig = null;

            try
            {
                savedConfig = JsonSerializer.Deserialize<SavedConfig>(File.ReadAllText("rr_config.txt"));
            }
            catch (FileNotFoundException) { }

            if (savedConfig == null)
            {
                savedConfig = new SavedConfig();
            }

            savedConfig.InitIds();
            rootNode = new GroupNode(savedConfig.RootNode);
        }

        static void SaveConfigToDisk()
        {
            SavedConfig savedConfig = new SavedConfig();
            savedConfig.RootNode = (SavedConfig.GroupNode)rootNode.ToSavedConfigNode();
            string jsonSavedConfig = JsonSerializer.Serialize(savedConfig, new JsonSerializerOptions() { WriteIndented = true });
            File.WriteAllText("rr_config.txt", jsonSavedConfig);
        }

        static void Main(string[] args)
        {
            LoadConfigFromDisk();

            Sdl2Window window;
            GraphicsDevice gd;

            // Create window, GraphicsDevice, and all resources necessary for the demo.

            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 1280, 720, WindowState.Normal, "RetroRoulette"),
                new GraphicsDeviceOptions(true, null, true, ResourceBindingModel.Improved, true, true),
                out window,
                out gd);

            ImGui.CreateContext();
            ImGuiIOPtr io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard |
                ImGuiConfigFlags.DockingEnable;
            io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

            font20 = io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 20);
            font30 = io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 30);
            font40 = io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 40);

            CommandList cl = gd.ResourceFactory.CreateCommandList();
            ImGuiController controller = new ImGuiController(gd, gd.MainSwapchain.Framebuffer.OutputDescription, window.Width, window.Height);

            window.Resized += () =>
            {
                gd.MainSwapchain.Resize((uint)window.Width, (uint)window.Height);
                controller.WindowResized(window.Width, window.Height);
            };

            Stopwatch dtStopwatch = Stopwatch.StartNew();

            // Main application loop
            while (window.Exists)
            {
                float deltaTime = (float) dtStopwatch.Elapsed.TotalSeconds;
                dtStopwatch.Restart();
                InputSnapshot snapshot = window.PumpEvents();
                if (!window.Exists) { break; }
                controller.Update(deltaTime, snapshot); // Feed the input events to our ImGui controller, which passes them through to ImGui.

                RenderUI();

                for (int i = 0; i < backgroundTasks.Count; i++)
                {
                    if (backgroundTasks[i].TryFinish())
                    {
                        backgroundTasks.RemoveAt(i);
                        i--;
                    }
                }

                cl.Begin();
                cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
                cl.ClearColorTarget(0, new RgbaFloat(1, 1, 1, 1));
                controller.Render(gd, cl);
                cl.End();
                gd.SubmitCommands(cl);
                gd.SwapBuffers(gd.MainSwapchain);
            }

            SaveConfigToDisk();

            // Clean up Veldrid resources
            gd.WaitForIdle();
            controller.Dispose();
            cl.Dispose();
            gd.Dispose();
        }

        static string browserSearch = "";

        static bool showGameSelCfg = false;

        static void RenderUI()
        {
            ImGuiViewportPtr viewportptr = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewportptr.WorkPos);
            ImGui.SetNextWindowSize(viewportptr.WorkSize);
            ImGui.SetNextWindowViewport(viewportptr.ID);

            if (ImGui.Begin("Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove))
            {
                if (ImGui.BeginTabBar("tabs"))
                {
                    ImGui.BeginDisabled(savedconfigRootNodePendingSave != null);

                    if (ImGui.BeginTabItem("Browser"))
                    {
                        RenderBrowserTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Roulette"))
                    {
                        RenderSlotMachineTab();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndDisabled();

                    if (ImGui.BeginTabItem("Config"))
                    {
                        RenderConfigTab();
                        ImGui.EndTabItem();
                    }

                    if (renderImguiDemo)
                    {
                        if (ImGui.BeginTabItem("ImGui Demo"))
                        {
                            ImGui.ShowDemoWindow();
                            ImGui.EndTabItem();
                        }
                    }
                    else
                    {
                        if (ImGui.IsKeyPressed(ImGuiKey.Home))
                        {
                            renderImguiDemo = true;
                        }
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }

        private static void RenderBrowserTab()
        {
            const float searchWidth = 300;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - searchWidth / 2));
            ImGui.SetNextItemWidth(searchWidth);
            ImGui.InputText("##search", ref browserSearch, 128);

            List<Game> matchingGames = rootNode.Games.Where(game => game.MatchesNameFilter(browserSearch)).ToList();

            ImGui.SameLine();
            ImGui.Text($"{matchingGames.Count}");

            if (ImGui.BeginTable("matches", 3, ImGuiTableFlags.ScrollY)) // TODO first col width
            {
                const int maxDisplayed = 100;

                foreach (Game game in matchingGames.Take(maxDisplayed)) // TODO allow more
                {
                    foreach (string variant in game.Variants())
                    {
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();

                        ImGui.TextUnformatted(game.ownerNode.name);

                        ImGui.TableNextColumn();

                        ImGui.TextUnformatted(game.name);

                        ImGui.TableNextColumn();

                        bool selected = false;
                        if (ImGui.Selectable($"{variant}##{game.name}/{game.ownerNode.name}", ref selected, ImGuiSelectableFlags.SpanAllColumns))
                            game.PlayVariant(variant);
                    }
                }

                ImGui.EndTable();
            }
        }

        private static void RenderSlotMachineTab()
        {
            bool showGameSelCfgThisFrame = showGameSelCfg;

            if (showGameSelCfgThisFrame)
            {
                ImGui.BeginTable("slotmachine", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV, ImGui.GetContentRegionAvail());
                ImGui.TableNextColumn();
            }

            Vector2 slotMachineTabTopRight = ImGui.GetCursorPos() + new Vector2(ImGui.GetContentRegionAvail().X, 0);

            RenderSlotMachine();

            // Show/hide selection config button
            {
                Vector2 cursorPosBefore = ImGui.GetCursorPos();

                ImGui.SetCursorPos(slotMachineTabTopRight + new Vector2(-50, 0));

                if (ImGui.Button(showGameSelCfgThisFrame ? ">" : "<", new Vector2(50, 50)))
                {
                    showGameSelCfg = !showGameSelCfg;
                }

                ImGui.SetCursorPos(cursorPosBefore);
            }

            if (showGameSelCfgThisFrame)
            {
                ImGui.TableNextColumn();

                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.07f, 0.07f, 0.07f, 1.0f));

                if (ImGui.BeginChild("selectionconfig", ImGui.GetContentRegionAvail()))
                {
                    RenderGameSelectionConfig();
                }
                ImGui.EndChild();

                ImGui.PopStyleColor();

                ImGui.EndTable();
            }
        }

        private static void RenderSlotMachine()
        {
            float spinStopButtonsSize;

            using (StyleContext sctxButtons = new StyleContext())
            {
                sctxButtons.SetStyleVar(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding * 2);
                sctxButtons.SetFont(font40);

                spinStopButtonsSize =
                    ImGui.CalcTextSize("Spin!").X +
                    ImGui.GetStyle().FramePadding.X * 2 +
                    ImGui.GetStyle().ItemSpacing.X +
                    ImGui.CalcTextSize("Stop!").X +
                    ImGui.GetStyle().FramePadding.X * 2;

                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - spinStopButtonsSize / 2));

                using (StyleContext sctxSpin = new StyleContext())
                {
                    sctxSpin.SetDisabled(reels.Any(reel => reel.spinning));

                    Vector4 color = new Vector4(4/255.0f, 141/255.0f, 6/255.0f, 1.0f);
                    sctxSpin.SetStyleColor(ImGuiCol.Button, color);
                    sctxSpin.SetStyleColor(ImGuiCol.ButtonHovered, color * 0.9f);
                    sctxSpin.SetStyleColor(ImGuiCol.ButtonActive, color * 0.7f);

                    if (ImGui.Button("Spin!"))
                    {
                        reels.Clear();

                        for (int i = 0; i < nReels; i++)
                        {
                            reels.Add(new Reel(rootNode.WeightedFilteredRandomGame()));
                        }

                        nextSpinTick = appStopwatch.Elapsed.TotalSeconds;
                    }
                }

                ImGui.SameLine();

                using (StyleContext sctxStop = new StyleContext())
                {
                    sctxStop.SetDisabled(!reels.Any(reel => reel.spinning));

                    Vector4 color = new Vector4(203/255.0f, 54/255.0f, 37/255.0f, 1.0f);
                    sctxStop.SetStyleColor(ImGuiCol.Button, color);
                    sctxStop.SetStyleColor(ImGuiCol.ButtonHovered, color * 0.9f);
                    sctxStop.SetStyleColor(ImGuiCol.ButtonActive, color * 0.7f);

                    if (ImGui.Button("Stop!"))
                    {
                        reels.First(reel => reel.spinning).spinning = false;
                    }
                }
            }

            {
                const float incDecBtnsWidth = 40;

                float cursorPosXStart = ImGui.GetCursorPosX();
                float contentRegionAvailXStart = ImGui.GetContentRegionAvail().X;

                ImGui.SetCursorPosX(cursorPosXStart + (contentRegionAvailXStart / 2 - spinStopButtonsSize / 2));

                ImGui.BeginDisabled(nReels == 1);

                if (ImGui.Button("<##decreel", new Vector2(incDecBtnsWidth, 0)))
                {
                    nReels--;
                }

                ImGui.EndDisabled();
                
                ////

                string nReelText = $"{nReels} reel{(nReels > 1 ? "s" : "")}";
                float nReelTextWidth = ImGui.CalcTextSize(nReelText).X;

                ImGui.SameLine();
                ImGui.SetCursorPosX(cursorPosXStart + (contentRegionAvailXStart / 2 - nReelTextWidth / 2));
                ImGui.TextUnformatted(nReelText);

                ////

                ImGui.SameLine();
                ImGui.SetCursorPosX(cursorPosXStart + (contentRegionAvailXStart / 2 + spinStopButtonsSize / 2 - incDecBtnsWidth));

                if (ImGui.Button(">##increel", new Vector2(incDecBtnsWidth, 0)))
                {
                    nReels++;
                }

                // If user adjusts reel count while spinning, adjust the reels themselves

                if (reels.Any(reel => reel.spinning))
                {
                    while (nReels > reels.Count)
                    {
                        reels.Add(new Reel(rootNode.WeightedFilteredRandomGame()));
                    }

                    while (nReels < reels.Count && reels.Last().spinning)
                    {
                        reels.RemoveAt(reels.Count - 1);
                    }
                }
            }

            float searchWidth = 300;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - searchWidth / 2));
            ImGui.SetNextItemWidth(searchWidth);
            ImGui.InputText("##search", ref gameNameFilter, 128);
            ImGui.SameLine();
            ImGui.Text($"{rootNode.FilteredEnabledGames.Count()}");

            const int nameColWidth = 400;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - nameColWidth / 2));

            if (ImGui.BeginTable("roms", 2))
            {
                if (reels.Any(reel => reel.spinning) && appStopwatch.Elapsed.TotalSeconds >= nextSpinTick)
                {
                    foreach (Reel reel in reels)
                    {
                        if (reel.spinning)
                        {
                            reel.Game = rootNode.WeightedFilteredRandomGame();
                        }
                    }

                    nextSpinTick = appStopwatch.Elapsed.TotalSeconds + spinTickTime;
                }

                ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, nameColWidth);
                ImGui.TableSetupColumn("##button", ImGuiTableColumnFlags.WidthFixed, 300);

                foreach (Reel reel in reels)
                {
                    Game? game = reel.Game;

                    ImGui.TableNextRow();

                    if (game != null)
                    {
                        if (reel.spinning)
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));

                        ImGui.TableNextColumn();
                        ImGui.PushFont(font30);
                        if (reel.spinning)
                        {
                            ImGui.Text(game.name);
                        }
                        else
                        {
                            ImGui.TextWrapped(game.name);
                        }
                        ImGui.PopFont();
                        ImGui.Text(game.ownerNode.name);

                        if (reel.spinning)
                            ImGui.PopStyleColor();

                        ImGui.Spacing();
                        ImGui.Spacing();

                        ImGui.TableNextColumn();

                        if (!reel.spinning)
                        {
                            ImGui.BeginDisabled(game.Variants().Count() == 1);

                            if (ImGui.BeginCombo($"##options{game.ownerNode.name}+{game.name}", reel.variant))
                            {
                                foreach (string variant in game.Variants())
                                {
                                    if (ImGui.Selectable(variant))
                                    {
                                        reel.variant = variant;
                                    }
                                }

                                ImGui.EndCombo();
                            }

                            ImGui.EndDisabled();

                            ImGui.BeginDisabled(!game.IsPlayable);

                            if (ImGui.Button($"Play##{game.ownerNode}+{game.name}"))
                                game.PlayVariant(reel.variant);

                            ImGui.EndDisabled();
                        }
                    }
                    else
                    {
                        ImGui.TableNextColumn();
                        ImGui.PushFont(font30);
                        ImGui.Text("[No games match filters]");
                        ImGui.PopFont();
                        ImGui.Text("");

                        ImGui.Spacing();
                        ImGui.Spacing();

                        ImGui.TableNextColumn();
                    }
                }

                ImGui.EndTable();
            }
        }

        private static void RenderGameSelectionConfig()
        {
            if (ImGui.Button("Reset"))
            {
                foreach (GamesNode gamesNode in rootNode.AllSubNodes().OfType<GamesNode>())
                {
                    gamesNode.Enabled = true;
                    gamesNode.ResetWeight();
                }
            }

            ImGui.Separator();

            if (ImGui.BeginTable("nodetree", 3))
            {
                ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, 250);
                ImGui.TableSetupColumn("##enable", ImGuiTableColumnFlags.WidthFixed, 25);
                ImGui.TableSetupColumn("##bar", ImGuiTableColumnFlags.WidthStretch);

                Stack<Queue<Node>> foldersStack = new Stack<Queue<Node>>();
                foldersStack.Push(new Queue<Node>(rootNode.subNodes));

                double rootNodeEffectiveWeight = rootNode.EffectiveWeight;

                while (foldersStack.Count > 0)
                {
                    if (foldersStack.Peek().TryDequeue(out Node? nextNode))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();

                        bool nodeOpen = false;

                        if (nextNode is GamesNode gamesNode)
                        {
                            ImGui.TreeNodeEx(gamesNode.name, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
                        }
                        else if (nextNode is GroupNode groupNode)
                        {
                            if (ImGui.TreeNode($"{groupNode.name}###{groupNode.id}"))
                            {
                                nodeOpen = true;
                                foldersStack.Push(new Queue<Node>(groupNode.subNodes));
                            }
                        }

                        ImGui.TableNextColumn();

                        bool enabled = nextNode.Enabled;

                        ImGui.PushItemFlag((ImGuiItemFlags)(1 << 12), enabled && nextNode is GroupNode groupNode2 && groupNode2.AllSubNodes().Any(sn => !sn.Enabled));

                        if (ImGui.Checkbox($"##{nextNode.name}", ref enabled))
                        {
                            nextNode.Enabled = enabled;
                        }

                        ImGui.PopItemFlag();

                        ImGui.TableNextColumn();

                        if (nodeOpen)
                        {
                            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                        }

                        double weightFraction = rootNodeEffectiveWeight > 0 ? nextNode.EffectiveWeight / rootNodeEffectiveWeight : 0;
                        string display = $"{100 * weightFraction:f1}% ({nextNode.FilteredGames.Count()} games)";

                        ImGui.ProgressBar((float)weightFraction, new Vector2(-10, 0f), nextNode.Enabled ? display : "(disabled)");

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                            nodeDraggedProgressBar = nextNode;

                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                            nodeDraggedProgressBar = null;

                        if (nodeDraggedProgressBar == nextNode)
                            nextNode.Weight *= Math.Pow(1.01, ImGui.GetIO().MouseDelta.X);

                        if (nodeOpen)
                        {
                            ImGui.PopStyleColor();
                            ImGui.PopStyleColor();
                            ImGui.PopStyleColor();
                        }
                    }
                    else
                    {
                        foldersStack.Pop();

                        if (foldersStack.Count > 0)
                            ImGui.TreePop();
                    }
                }

                ImGui.EndTable();
            }
        }

        static SavedConfig.GroupNode? savedconfigRootNodePendingSave = null;

        private static void RenderConfigTab()
        {
            if (ImGui.BeginTable("nodetree", 3))
            {
                ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.IndentEnable, 300); // TODO dynamic size
                ImGui.TableSetupColumn("buttons", ImGuiTableColumnFlags.WidthFixed, 207);
                ImGui.TableSetupColumn("commands", ImGuiTableColumnFlags.WidthStretch);

                using (StyleContext sctxNodetree = new StyleContext())
                {
                    sctxNodetree.SetStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));
                    sctxNodetree.SetStyleVar(ImGuiStyleVar.CellPadding, new Vector2(1, 1));

                    SavedConfig.GroupNode savedconfigRootNodeRender = savedconfigRootNodePendingSave ?? (SavedConfig.GroupNode)rootNode.ToSavedConfigNode();

                    bool edited = false;
                    RenderConfigTabTree(savedconfigRootNodeRender, ref edited, null, savedconfigRootNodeRender);

                    if (edited && savedconfigRootNodePendingSave == null)
                    {
                        savedconfigRootNodePendingSave = savedconfigRootNodeRender;
                    }
                }

                ImGui.EndTable();
            }

            ImGui.BeginDisabled(savedconfigRootNodePendingSave == null);

            bool saveHotkeyPressed = (ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyReleased(ImGuiKey.S));

            if (ImGui.Button("Save") || (savedconfigRootNodePendingSave != null && saveHotkeyPressed))
            {
                rootNode = new GroupNode(savedconfigRootNodePendingSave);
                SaveConfigToDisk();

                savedconfigRootNodePendingSave = null;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                savedconfigRootNodePendingSave = null;
            }

            ImGui.EndDisabled();
        }

        static Dictionary<string, string[]> pathToFileTypes = new Dictionary<string, string[]>();

        public static IEnumerable<string> GetFileTypesInDir(string dirPath)
        {
            // TODO periodically refresh
            // TODO catch dir does not exist exception

            if (!pathToFileTypes.ContainsKey(dirPath))
            {
                string[] exts = Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories).Select(filePath => Path.GetExtension(filePath).ToLower()).Distinct().ToArray();
                Array.Sort(exts);
                pathToFileTypes[dirPath] = exts;
            }

            return pathToFileTypes[dirPath];
        }

        private static void RenderConfigTabTree(SavedConfig.Node node, ref bool edited, SavedConfig.GroupNode? parentNode, SavedConfig.GroupNode rootNode)
        {
            ImGui.PushID(node.id);

            bool renderChildren = false;

            ImGui.TableNextRow();

            float dragAreaYMin = ImGui.GetCursorScreenPos().Y;

            ImGui.TableNextColumn();

            {
                if (node is SavedConfig.GamesNode gamesNode)
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetTreeNodeToLabelSpacing() + ImGui.GetStyle().ItemSpacing.X);

                    ImGui.SetNextItemWidth(-float.Epsilon);
                    ImGui.InputText("##nameedit", ref gamesNode.NameEditable, 128);

                    if (ImGui.IsItemEdited())
                        edited = true;
                }
                else if (node is SavedConfig.GroupNode groupNode)
                {
                    if (node == rootNode)
                    {
                        ImGui.SetNextItemOpen(true);
                        ImGui.BeginDisabled();
                        ImGui.TreeNodeEx("##treenode", ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.FramePadding);
                        ImGui.EndDisabled();
                        renderChildren = true;
                    }
                    else
                    {
                        renderChildren = ImGui.TreeNodeEx("##treenode", ImGuiTreeNodeFlags.AllowOverlap | ImGuiTreeNodeFlags.FramePadding);

                        ImGui.SameLine();

                        ImGui.SetNextItemWidth(-float.Epsilon);
                        ImGui.InputText("##nameedit", ref groupNode.NameEditable, 128);

                        if (ImGui.IsItemEdited())
                            edited = true;
                    }
                }
            }

            ImGui.TableNextColumn();

            bool handleDrag = false;

            if (node != rootNode)
            {
                ImGui.Button($"^ v");

                if (ImGui.IsItemActive() && !ImGui.IsItemHovered())
                {
                    handleDrag = true;
                }

                ImGui.SameLine();

                if (ImGui.Button("Move to..."))
                {
                    ImGui.OpenPopup("moveto");
                }

                if (ImGui.IsPopupOpen("moveto") && ImGui.BeginPopup("moveto"))
                {
                    if (ImGui.Selectable("(root)"))
                    {
                        parentNode.ChildNodes.Remove(node);
                        rootNode.ChildNodes.Add(node);
                        edited = true;
                    }

                    void RenderMenu(SavedConfig.GroupNode menuNode, ref bool edited)
                    {
                        foreach (SavedConfig.Node menuNodeChild in new List<SavedConfig.Node>(menuNode.ChildNodes))
                        {
                            if (menuNodeChild is SavedConfig.GroupNode groupNodeChild)
                            {
                                bool isThisNode = groupNodeChild == node;

                                if (!isThisNode && groupNodeChild.ChildNodes.Any(c => c is SavedConfig.GroupNode))
                                {
                                    if (ImGui.BeginMenu($"{groupNodeChild.Name}##{groupNodeChild.id}"))
                                    {
                                        RenderMenu(groupNodeChild, ref edited);
                                        ImGui.EndMenu();
                                    }
                                }
                                else
                                {
                                    ImGui.BeginDisabled(isThisNode);
                                    ImGui.Selectable($"{groupNodeChild.Name}##{groupNodeChild.id}");
                                    ImGui.EndDisabled();
                                }

                                if (ImGui.IsItemClicked())
                                {
                                    parentNode.ChildNodes.Remove(node);
                                    groupNodeChild.ChildNodes.Add(node);
                                    edited = true;
                                }
                            }
                        }
                    }

                    RenderMenu(rootNode, ref edited);

                    ImGui.EndPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Delete"))
                {
                    parentNode.ChildNodes.Remove(node);
                    edited = true;
                }

                ImGui.SameLine();
            }

            {
                if (node is SavedConfig.GroupNode groupNode)
                {
                    if (ImGui.Button("Add..."))
                    {
                        ImGui.OpenPopup("add");
                    }

                    if (ImGui.IsPopupOpen("add") && ImGui.BeginPopup("add"))
                    {
                        static int MaxId(SavedConfig.Node node)
                        {
                            if (node is SavedConfig.GroupNode groupNode && groupNode.ChildNodes.Any())
                            {
                                return Math.Max(node.id, groupNode.ChildNodes.Max(MaxId));
                            }
                            else
                            {
                                return node.id;
                            }
                        };

                        if (ImGui.Selectable("Group"))
                        {
                            groupNode.ChildNodes.Add(new SavedConfig.GroupNode() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        if (ImGui.Selectable("File Folder"))
                        {
                            groupNode.ChildNodes.Add(new SavedConfig.FileFolderNode() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        if (ImGui.Selectable("Name List"))
                        {
                            groupNode.ChildNodes.Add(new SavedConfig.NameListNode() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        if (ImGui.Selectable("MAME"))
                        {
                            groupNode.ChildNodes.Add(new SavedConfig.MAMENode() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        ImGui.EndPopup();
                    }
                }
            }

            ImGui.TableNextColumn();

            {
                if (node is SavedConfig.GamesNode gamesNode)
                {
                    gamesNode.RenderConfigEditor(ref edited);
                }
            }

            ImGui.PopID();

            if (renderChildren)
            {
                if (node is SavedConfig.GroupNode groupNode)
                {
                    foreach (SavedConfig.Node childNode in new List<SavedConfig.Node>(groupNode.ChildNodes))
                    {
                        RenderConfigTabTree(childNode, ref edited, groupNode, rootNode);
                    }

                    ImGui.TreePop();
                }
            }

            float dragAreaYMax = ImGui.GetCursorScreenPos().Y + ImGui.GetStyle().CellPadding.Y;
            
            if (handleDrag)
            {
                if (ImGui.GetMousePos().Y < dragAreaYMin)
                {
                    int i = parentNode.ChildNodes.IndexOf(node);

                    if (i > 0)
                    {
                        parentNode.ChildNodes.RemoveAt(i);
                        parentNode.ChildNodes.Insert(i - 1, node);
                        edited = true;
                    }
                }
                else if (ImGui.GetMousePos().Y > dragAreaYMax)
                {
                    int i = parentNode.ChildNodes.IndexOf(node);

                    if (i < (parentNode.ChildNodes.Count - 1))
                    {
                        parentNode.ChildNodes.RemoveAt(i);
                        parentNode.ChildNodes.Insert(i + 1, node);
                        edited = true;
                    }
                }
            }
        }
    }
}