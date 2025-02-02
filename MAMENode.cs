using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using static RetroRoulette.AppState;

namespace RetroRoulette
{
    public class SharedMAMEState
    {
        public string MAMEPath { get; set; } = "";
        public List<MAMESystemInfo> MAMESystems { get; set; } = new List<MAMESystemInfo>();
    }

    public class MAMENodeState : GamesNodeState
    {
        private string exePath = "";
        public string ExePath { get => exePath; set => exePath = value; }

        public override NodeState Clone()
        {
            return new MAMENodeState
            {
                id = this.id,
                Name = this.Name,
                Enabled = this.Enabled,
                Weight = this.Weight,
                ExePath = this.ExePath,
            };
        }

        public override void RenderConfigEditor(ref bool edited)
        {
            ImGui.SetNextItemWidth(400);
            ImGui.InputText("##pathedit", ref exePath, 512);

            if (ImGui.IsItemEdited())
                edited = true;

            //ImGui.SameLine();

            //if (ImGui.Button("Refresh systems list (will take a bit)"))
            //{
            //    Program.AddBackgroundTask(new MAMESystemsBgTask(this));
            //}

            //if (mameSystemsBgTask != null && mameSystemsBgTask.IsRunning)
            //{
            //    edited = true;
            //}
        }
    }

    class MAMENode : GamesNode
    {
        public MAMENodeState State => (this.GamesNodeState as MAMENodeState)!;

        private List<MAMESystemGroup> games;
        public override IEnumerable<Game> Games => games;

        public MAMENode(MAMENodeState mameNode)
            : base(mameNode)
        {
            // How to combine variants of games without ignoring interesting bootlegs? maybe base it on manufacturer?

            // TODO refresh this when we refresh the list of games

            // TODO filtering/weighting for this node?

            this.games = Program.appState.SharedMameState.MAMESystems
                .GroupBy(s => (ParentName: s.CloneOf ?? s.ShortName, s.NameCore))
                .Select(grp => new MAMESystemGroup(grp.Key.NameCore, grp.Key.ParentName, grp, this))
                .ToList();

            if (mameNode.Weight == 0)
            {
                mameNode.Weight = games.Count;
            }
        }
    }

    public class MAMESystemInfo
    {
        public string ShortName { get; set; }
        public string? CloneOf { get; set; }
        public string NameCore { get; set; }
        public string NameVariantInfo { get; set; }
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

            string description = machineNode["description"].InnerText;
            int parenIndex = description.IndexOf('(');
            NameCore = parenIndex >= 0 ? description.Substring(0, parenIndex - 1) : description;
            NameVariantInfo = parenIndex >= 0 ? description.Substring(parenIndex) : "";

            string year = machineNode["year"].InnerText;

            if (!int.TryParse(year, out normalizedYear))
            {
                if (year == "19??" || year == "????")
                {
                    normalizedYear = 1980; // Lean early since unknown
                }
                else if (year == "20??")
                {
                    normalizedYear = 2000; // Lean early since unknown
                }
                else if (year[3] == '?')
                {
                    normalizedYear = int.Parse(year.Substring(0, 3) + "5");
                }
                else if (year.Length == 5 && year.EndsWith("?"))
                {
                    normalizedYear = int.Parse(year.Substring(0, 4));
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

    //class MAMESystemsBgTask : BackgroundTask
    //{
    //    private MAMENodeState mameNode;
    //    private string exePath;
    //    private List<MAMESystemInfo> systems = new List<MAMESystemInfo>();

    //    public MAMESystemsBgTask(MAMENodeState mameNode)
    //    {
    //        this.mameNode = mameNode;
    //        this.exePath = mameNode.ExePath;
    //        StartWorkThread();
    //    }

    //    protected override void DoWork()
    //    {
    //        string verifyromsOutput;

    //        //if (File.Exists("verifyroms.txt"))
    //        //{
    //        //    verifyromsOutput = File.ReadAllText("verifyroms.txt");
    //        //}
    //        //else
    //        {
    //            using (Process pVerifyroms = new Process())
    //            {
    //                pVerifyroms.StartInfo.FileName = exePath;
    //                pVerifyroms.StartInfo.ArgumentList.Add("-verifyroms");
    //                pVerifyroms.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
    //                pVerifyroms.StartInfo.CreateNoWindow = true;
    //                pVerifyroms.StartInfo.UseShellExecute = false;
    //                pVerifyroms.StartInfo.RedirectStandardOutput = true;

    //                pVerifyroms.Start();
    //                verifyromsOutput = pVerifyroms.StandardOutput.ReadToEnd();
    //                pVerifyroms.WaitForExit();
    //            }

    //            File.WriteAllText("verifyroms.txt", verifyromsOutput);
    //        }

    //        ImmutableHashSet<string> playableSets = verifyromsOutput
    //            .Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
    //            .Select(line => line.Trim())
    //            .Where(line => line.StartsWith("romset ") && !line.EndsWith(" is bad"))
    //            .Select(line => line.Split())
    //            .Where(words => words.Length >= 2)
    //            .Select(words => words[1])
    //            .ToImmutableHashSet();

    //        XmlDocument xmldoc = new XmlDocument();

    //        //if (File.Exists("listxml.txt"))
    //        //{
    //        //    xmldoc.Load("listxml.txt");
    //        //}
    //        //else
    //        {
    //            using (Process pListxml = new Process())
    //            {
    //                pListxml.StartInfo.FileName = exePath;
    //                pListxml.StartInfo.ArgumentList.Add("-listxml");
    //                pListxml.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
    //                pListxml.StartInfo.CreateNoWindow = true;
    //                pListxml.StartInfo.UseShellExecute = false;
    //                pListxml.StartInfo.RedirectStandardOutput = true;

    //                pListxml.Start();

    //                string xml = pListxml.StandardOutput.ReadToEnd();
    //                File.WriteAllText("listxml.txt", xml);
    //                xmldoc.LoadXml(xml);

    //                pListxml.WaitForExit();
    //            }
    //        }

    //        XmlNode mameXmlNode = xmldoc.GetElementsByTagName("mame")[0];

    //        systems = new List<MAMESystemInfo>();

    //        foreach (XmlNode machineNode in mameXmlNode.ChildNodes)
    //        {
    //            // Ignore anything the user doesn't have the ROMs for.

    //            string? name = machineNode.Attributes?["name"]?.Value;

    //            if (name == null || !playableSets.Contains(name))
    //                continue;

    //            // Ignore anything that can't run. Pretty straightforward.
    //            // Note: "isdevice" seems to imply runnable="no" so this de facto excludes all devices

    //            if (machineNode.Attributes?["runnable"]?.Value == "no")
    //                continue;

    //            // Ignore any drivers which aren't working

    //            if (machineNode["driver"]?.Attributes?["status"]?.Value == "preliminary")
    //                continue;

    //            // After the above filters, there are only like two 0-player games - ignore em!

    //            if (machineNode["input"]?.Attributes?["players"]?.Value == "0")
    //                continue;

    //            // BIOSes are not super exciting and there are also very few of them

    //            if (machineNode.Attributes?["isbios"]?.Value == "yes")
    //                continue;

    //            systems.Add(new MAMESystemInfo(machineNode));
    //        }
    //    }

    //    protected override void OnWorkComplete()
    //    {
    //        mameNode.Systems = this.systems;
    //    }
    //}

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

            p.StartInfo.FileName = (ownerNode as MAMENode).State.ExePath;
            p.StartInfo.ArgumentList.Add(shortName);

            // MAME looks for paths on the working directory (annoying)
            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(p.StartInfo.FileName);
            p.Start();
        }
    }
}
