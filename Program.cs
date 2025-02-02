using ImGuiNET;
using SharpGen.Runtime.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
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
using static RetroRoulette.AppState;

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
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
    [JsonDerivedType(typeof(FileFolderNodeState), "FileFolder")]
    [JsonDerivedType(typeof(GroupNodeState), "Group")]
    [JsonDerivedType(typeof(NameListNodeState), "NameList")]
    [JsonDerivedType(typeof(MAMENodeState), "MAME")]
    public abstract class NodeState
    {
        public int id = 0;
        private string name = "";

        [JsonPropertyOrder(-2)]
        public string Name { get => name; set => name = value; }

        [JsonIgnore]
        public ref string NameEditable => ref name;

        public abstract NodeState Clone();
    }

    public abstract class GamesNodeState : NodeState
    {
        [JsonPropertyOrder(-1)]
        public bool Enabled { get; set; } = true;
        [JsonPropertyOrder(-1)]
        public double Weight { get; set; } = 0;

        public abstract void RenderConfigEditor(ref bool edited);
    }

    public class GroupNodeState : NodeState
    {
        public List<NodeState> ChildNodes { get; set; } = new List<NodeState>();

        public override NodeState Clone()
        {
            return new GroupNodeState 
            {
                id = this.id,
                Name = this.Name,
                ChildNodes = ChildNodes.Select(c => c.Clone()).ToList(),
            };
        }
    }

    // Anything with "state" at the end is state that will be saved to disk and loaded on start

    public class AppState
    {
        public GroupNodeState RootNode { get; set; } = new GroupNodeState();
        public SharedMAMEState SharedMameState { get; set; } = new SharedMAMEState();

        /////////////////////////////////////////////////////////////////////////

        public void InitIds()
        {
            int nextId = 1;
            InitIdsInternal(RootNode, ref nextId);
        }

        private static void InitIdsInternal(NodeState node, ref int nextId)
        {
            Debug.Assert(node.id == 0);
            node.id = nextId++;

            if (node is GroupNodeState groupNode)
            {
                foreach (NodeState childNode in groupNode.ChildNodes)
                {
                    InitIdsInternal(childNode, ref nextId);
                }
            }
        }
    }

    abstract class Node
    {
        public NodeState NodeState { get; set; }

        public Node(NodeState nodeState)
        {
            this.NodeState = nodeState;
        }

        public abstract bool Enabled { get; set; }
        public abstract double Weight { get; set; }
        public abstract double EffectiveWeight { get; }

        public abstract IEnumerable<Game> Games { get; }
        public IEnumerable<Game> FilteredGames => Games.Where(game => game.MatchesNameFilter(Program.gameNameFilter));
        public IEnumerable<Game> FilteredEnabledGames => Games.Where(game => game.ownerNode.Enabled && game.MatchesNameFilter(Program.gameNameFilter));
        public bool AnyGamesMatchFilter => Games.Any(game => game.MatchesNameFilter(Program.gameNameFilter));
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

    abstract class GamesNode : Node
    {
        public GamesNodeState GamesNodeState => (this.NodeState as GamesNodeState)!;

        public override bool Enabled { get => GamesNodeState.Enabled; set => GamesNodeState.Enabled = value; }
        public override double Weight { get => GamesNodeState.Weight; set => GamesNodeState.Weight = value; }
        public override double EffectiveWeight => (Enabled && AnyGamesMatchFilter) ? Weight : 0;

        public GamesNode(GamesNodeState savedConfigGamesNode)
            : base(savedConfigGamesNode)
        {
        }

        public void ResetWeight()
        {
            Weight = Games.Count();
        }
    }

    class GroupNode : Node
    {
        public GroupNodeState GroupNodeState => (this.NodeState as GroupNodeState)!;

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

        public GroupNode(GroupNodeState savedConfigGroupNode)
            : base(savedConfigGroupNode)
        {
            subNodes = new List<Node>();

            foreach (NodeState childNode in savedConfigGroupNode.ChildNodes)
            {
                if (childNode is GroupNodeState childGroupNode)
                {
                    subNodes.Add(new GroupNode(childGroupNode));
                }
                else if (childNode is FileFolderNodeState childFileFolderNode)
                {
                    subNodes.Add(new FileFolderNode(childFileFolderNode));
                }
                else if (childNode is NameListNodeState childNameListNode)
                {
                    subNodes.Add(new NameListNode(childNameListNode));
                }
                else if (childNode is MAMENodeState childMameNode)
                {
                    subNodes.Add(new MAMENode(childMameNode));
                }
            }
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

        public static AppState appState = new AppState();
        static GroupNode rootNode; // TODO
        public static string gameNameFilter = "";
        static Node? nodeDraggedProgressBar = null;

        public static List<BackgroundTask> backgroundTasks = new List<BackgroundTask>();

        public static void AddBackgroundTask(BackgroundTask task)
        { 
            backgroundTasks.Add(task); 
        }

        static void LoadConfigFromDisk()
        {
            try
            {
                appState = JsonSerializer.Deserialize<AppState>(File.ReadAllText("rr_config.txt"));
                appState.InitIds(); // TODO
            }
            catch (FileNotFoundException) 
            {
                appState = new AppState();
            }

            rootNode = new GroupNode(appState.RootNode);
        }

        static void SaveConfigToDisk()
        {
            string jsonSavedConfig = JsonSerializer.Serialize(appState, new JsonSerializerOptions() { WriteIndented = true });
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

                        ImGui.TextUnformatted(game.ownerNode.NodeState.Name);

                        ImGui.TableNextColumn();

                        ImGui.TextUnformatted(game.name);

                        ImGui.TableNextColumn();

                        bool selected = false;
                        if (ImGui.Selectable($"{variant}##{game.name}/{game.ownerNode.NodeState.Name}", ref selected, ImGuiSelectableFlags.SpanAllColumns))
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
                        ImGui.Text(game.ownerNode.NodeState.Name);

                        if (reel.spinning)
                            ImGui.PopStyleColor();

                        ImGui.Spacing();
                        ImGui.Spacing();

                        ImGui.TableNextColumn();

                        if (!reel.spinning)
                        {
                            ImGui.BeginDisabled(game.Variants().Count() == 1);

                            if (ImGui.BeginCombo($"##options{game.ownerNode.NodeState.Name}+{game.name}", reel.variant))
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
                            ImGui.TreeNodeEx(gamesNode.GamesNodeState.Name, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
                        }
                        else if (nextNode is GroupNode groupNode)
                        {
                            if (ImGui.TreeNode($"{groupNode.NodeState.Name}###{groupNode.NodeState.id}"))
                            {
                                nodeOpen = true;
                                foldersStack.Push(new Queue<Node>(groupNode.subNodes));
                            }
                        }

                        ImGui.TableNextColumn();

                        bool enabled = nextNode.Enabled;

                        ImGui.PushItemFlag((ImGuiItemFlags)(1 << 12), enabled && nextNode is GroupNode groupNode2 && groupNode2.AllSubNodes().Any(sn => !sn.Enabled));

                        if (ImGui.Checkbox($"##{nextNode.NodeState.Name}", ref enabled))
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

        static GroupNodeState? savedconfigRootNodePendingSave = null;

        private static void RenderConfigTab()
        {
            bool edited = false;
            GroupNodeState savedconfigRootNodeRender = savedconfigRootNodePendingSave ?? (appState.RootNode.Clone() as GroupNodeState)!;

            if (ImGui.BeginTable("nodetree", 3))
            {
                ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.IndentEnable, 300); // TODO dynamic size
                ImGui.TableSetupColumn("buttons", ImGuiTableColumnFlags.WidthFixed, 207);
                ImGui.TableSetupColumn("commands", ImGuiTableColumnFlags.WidthStretch);

                using (StyleContext sctxNodetree = new StyleContext())
                {
                    sctxNodetree.SetStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));
                    sctxNodetree.SetStyleVar(ImGuiStyleVar.CellPadding, new Vector2(1, 1));

                    RenderConfigTabTree(savedconfigRootNodeRender, ref edited, null, savedconfigRootNodeRender);
                }

                ImGui.EndTable();
            }

            if (ImGui.IsItemEdited())
                edited = true;

            if (edited && savedconfigRootNodePendingSave == null)
            {
                savedconfigRootNodePendingSave = savedconfigRootNodeRender;
            }

            ImGui.Separator();

            ImGui.BeginDisabled(savedconfigRootNodePendingSave == null);

            bool saveHotkeyPressed = (ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyReleased(ImGuiKey.S));

            if (ImGui.Button("Save") || (savedconfigRootNodePendingSave != null && saveHotkeyPressed))
            {
                appState.RootNode = savedconfigRootNodeRender;
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

        private static void RenderConfigTabTree(NodeState node, ref bool edited, GroupNodeState? parentNode, GroupNodeState rootNode)
        {
            ImGui.PushID(node.id);

            bool renderChildren = false;

            ImGui.TableNextRow();

            float dragAreaYMin = ImGui.GetCursorScreenPos().Y;

            ImGui.TableNextColumn();

            {
                if (node is GamesNodeState gamesNode)
                {
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetTreeNodeToLabelSpacing() + ImGui.GetStyle().ItemSpacing.X);

                    ImGui.SetNextItemWidth(-float.Epsilon);
                    ImGui.InputText("##nameedit", ref gamesNode.NameEditable, 128);

                    if (ImGui.IsItemEdited())
                        edited = true;
                }
                else if (node is GroupNodeState groupNode)
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

                    void RenderMenu(GroupNodeState menuNode, ref bool edited)
                    {
                        foreach (NodeState menuNodeChild in new List<NodeState>(menuNode.ChildNodes))
                        {
                            if (menuNodeChild is GroupNodeState groupNodeChild)
                            {
                                bool isThisNode = groupNodeChild == node;

                                if (!isThisNode && groupNodeChild.ChildNodes.Any(c => c is GroupNodeState))
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
                if (node is GroupNodeState groupNode)
                {
                    if (ImGui.Button("Add..."))
                    {
                        ImGui.OpenPopup("add");
                    }

                    if (ImGui.IsPopupOpen("add") && ImGui.BeginPopup("add"))
                    {
                        static int MaxId(NodeState node)
                        {
                            if (node is GroupNodeState groupNode && groupNode.ChildNodes.Any())
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
                            groupNode.ChildNodes.Add(new GroupNodeState() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        if (ImGui.Selectable("File Folder"))
                        {
                            groupNode.ChildNodes.Add(new FileFolderNodeState() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        if (ImGui.Selectable("Name List"))
                        {
                            groupNode.ChildNodes.Add(new NameListNodeState() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        if (ImGui.Selectable("MAME"))
                        {
                            groupNode.ChildNodes.Add(new MAMENodeState() { id = MaxId(rootNode) + 1 });
                            edited = true;
                        }

                        ImGui.EndPopup();
                    }
                }
            }

            ImGui.TableNextColumn();

            {
                if (node is GamesNodeState gamesNode)
                {
                    gamesNode.RenderConfigEditor(ref edited);
                }
            }

            ImGui.PopID();

            if (renderChildren)
            {
                if (node is GroupNodeState groupNode)
                {
                    foreach (NodeState childNode in new List<NodeState>(groupNode.ChildNodes))
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