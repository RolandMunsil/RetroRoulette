using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static RetroRoulette.AppState;

namespace RetroRoulette
{
    public class FileFolderNodeState : GamesNodeState
    {
        private string dirPath = "";
        public string DirPath { get => dirPath; set => dirPath = value; }
        public List<string> PlayCommand { get; set; } = new List<string>();
        public List<string> SupportedExtensions { get; set; } = new List<string>();

        public override NodeState Clone()
        {
            return new FileFolderNodeState
            {
                id = this.id,
                Name = this.Name,
                Enabled = this.Enabled,
                Weight = this.Weight,
                DirPath = this.DirPath,
                PlayCommand = new List<string>(this.PlayCommand),
                SupportedExtensions = new List<string>(this.SupportedExtensions),
            };
        }

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

    class FileFolderNode : GamesNode
    {
        public FileFolderNodeState State => (this.GamesNodeState as FileFolderNodeState)!;

        private List<ROMSet> romsets;
        public override IEnumerable<Game> Games => romsets;

        public FileFolderNode(FileFolderNodeState savedConfigFileFolderNode)
            : base(savedConfigFileFolderNode)
        {
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
                if (Directory.Exists(fileFolderNode.State.DirPath))
                {
                    romsets = Directory.EnumerateFiles(fileFolderNode.State.DirPath, "*", SearchOption.AllDirectories)
                    .Where(filePath => fileFolderNode.State.SupportedExtensions.Contains(Path.GetExtension(filePath)))
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
        public override bool IsPlayable => (ownerNode as FileFolderNode).State.PlayCommand.Count() > 0;
        public override void PlayVariant(string variant)
        {
            ROM rom = roms.First(rom => rom.details.PropsString() == variant);

            string[] command = (ownerNode as FileFolderNode).State.PlayCommand.Append(rom.path).ToArray();

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
}
