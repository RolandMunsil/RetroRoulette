using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static RetroRoulette.AppState;

namespace RetroRoulette
{
    public class NameListNodeState : GamesNodeState
    {
        public List<string> NameList { get; set; } = new List<string>();
        public List<string> PlayCommand { get; set; } = new List<string>();

        public override NodeState Clone()
        {
            return new NameListNodeState
            {
                id = this.id,
                Name = this.Name,
                Enabled = this.Enabled,
                Weight = this.Weight,
                NameList = new List<string>(this.NameList),
                PlayCommand = new List<string>(this.PlayCommand),
            };
        }

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

    class NameListNode : GamesNode
    {
        public NameListNodeState State => (this.GamesNodeState as NameListNodeState)!;

        private readonly List<SimpleGame> games;
        public override IEnumerable<Game> Games => games;

        public NameListNode(NameListNodeState savedConfigNameListNode)
            : base(savedConfigNameListNode)
        {
            games = savedConfigNameListNode.NameList.Select(s => new SimpleGame(s, this, savedConfigNameListNode.PlayCommand.ToArray())).ToList();

            if (Weight == 0)
            {
                Weight = games.Count;
            }
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
}
