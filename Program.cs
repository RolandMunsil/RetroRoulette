using ImGuiNET;
using System;
using System.Collections.Generic;
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
using System.Xml.Linq;
using Veldrid;
using Veldrid.MetalBindings;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Vulkan.Xlib;

namespace RetroRoulette
{
    class SavedConfig
    {
        public class Node
        {
            public string Name { get; set; } = "";
            public bool Enabled { get; set; } = true;
            public double Weight { get; set; } = 0;
            public string? DirPath { get; set; } = null;
            public List<Node> ChildNodes { get; set; } = new List<Node>();
        }

        public Node RootNode { get; set; } = new Node();
    }

    class ROM
    {
        public string path;
        public RomInfo details;
        private string folderName;

        public string Ext => Path.GetExtension(this.path);

        public ROM(string path, string folderName)
        {
            this.path = path;
            details = ROMNameParser.Parse(Path.GetFileNameWithoutExtension(this.path));
            this.folderName = folderName;
        }

        private string[]? GetPlayCommand()
        {
            return (folderName, Ext) switch
            {
                // Ideas for stuff to add support for:
                //  - PC games
                //     - Windows games (old '95/'98, more modern 2000s)
                //     - MS-DOS games
                //     - MSX?
                //     - Apple II (and etc.)
                //     - Amiga
                //     - Macintosh (shareware?)
                //     - Commodore 64?
                //     - Others? ZX Spectrum?
                //  - MAME arcade games
                //  - Steam games (just locally installed? need to figure out if there's a way to get the local user's full list of owned games)
                //  - Bluemaxima Flashpoint? Open up the SQLite databse in Data/. Maybe use the main curated list as an optional filter?
                //  - Other random weird systems: Game Master, GP32, Pokemon Mini, Tiger Game.com, Watara Supervision,
                //    LeapFrog Leapster, VTech V.Smile, Amiga CD32, Commodore CDTV, Funtech Super A'Can, 
                //    Atari 7800, Philips Videopac+ G7400, Sega SG-1000, Super Casette Vision, Arcadia 2001,
                //    Atari 2600, Atari 5200, ColecoVision, Entex Adventure Vision, Fairchild Channel F, Intellivision
                //    Magnavox Odyssey2, RCA Studio II, Vectrex, VTech Creativision. Others?

                // Feature/improvement ideas:
                // - multithread startup
                // - tighten up weight adjustment UI. Maybe just need to make it less wide?
                // - list roms that weren't playable
                // - save weights to file?
                // - allow adjusting weights of non-open nodes, just indicate they're different somehow.
                // - option to hide names but not systems (in reels)
                // - filters for additional flags on ROMs, and regions. E.g. only play unlicensed games, or only play Korean games
                // - some way of creating custom tags/collections? would be cool to do like a "Classics of Game" night
                // - support using no-intro, redump, etc xmls to get more info about ROMs?
                // - config specifies folder structure and folder -> node mappings
                // - use xmls to group games with different names? maybe even group games across multiple systems?

                ("Gamecube" or "Wii (Discs)" or "Wii (WiiWare)", ".rvz" or ".wbfs" or ".wad")
                    => new[] { "C:\\Portable Programs\\Dolphin\\Dolphin.exe", "-e", path },
                ("Playstation 2", ".bin" or ".iso")
                    => new[] { "C:\\Program Files\\PCSX2\\pcsx2-qt.exe", path },
                ("Dreamcast", ".cue")
                    => new[] { "C:\\Portable Programs\\Flycast\\flycast.exe", path },
                ("Xbox", ".iso")
                    => new[] { "C:\\Portable Programs\\Xemu\\xemu.exe", path },
                // NOTE: sounds like Ares is also a good option for PC Engine?
                // NOTE: lynx roms need a BIOS? and also to be converted for mednafen?
                (_, ".vb" or ".pce" or ".lnx")
                or ("Sega Saturn", ".cue")
                    => new[] { "C:\\Portable Programs\\Mednafen & Mednaffe\\mednafen.exe", path },
                ("Playstation", ".cue" or ".chd")
                    => new[] { "C:\\Portable Programs\\DuckStation\\duckstation-qt-x64-ReleaseLTCG.exe", path },
                // NOTE: Genesis Plus GX or MAME(?) might have better support for Sega Master System games
                // NOTE: Blastem or Genesis Plus GX might provide better emulation of Mega Drive games
                (_, ".z64" or ".ndd" or ".sfc" or ".st" or ".bs" or ".32x" or ".sc" or ".ws" or ".wsc" or ".ngc" or ".ngp")
                or ("Sega Master System", ".sms")
                or ("Sega CD", ".cue")
                or (not "Sega PICO", ".md")
                    => new[] { "C:\\Portable Programs\\ares-v132\\ares.exe", path },
                (_, ".j64")
                    => new[] { "C:\\Portable Programs\\BigPEmu\\BigPEmu.exe", path },
                (_, ".nes" or ".fds")
                    => new[] { "C:\\Portable Programs\\Mesen\\Mesen.exe", path },
                (_, ".gb" or ".gbc")
                    => new[] { "C:\\Portable Programs\\SameBoy\\sameboy.exe", path },
                (_, ".gba")
                    => new[] { "C:\\Portable Programs\\mGBA\\mGBA.exe", path },
                ("CD-i", ".chd")
                    => new[] { "C:\\Portable Programs\\MAME\\mame.exe", "cdimono1", "-cdrm", path },
                ("3DO", ".cue")
                    => new[] { "C:\\RetroArch-Win64\\retroarch.exe", "-L", "opera", path },
                _ => null,
            };
        }

        public bool CanPlay => GetPlayCommand() != null;

        public void Play()
        {
            Debug.Assert(CanPlay);
            string[] command = GetPlayCommand()!;

            Process p = new Process();

            p.StartInfo.FileName = command[0];
            foreach (string arg in command.Skip(1))
                p.StartInfo.ArgumentList.Add(arg);

            // MAME looks for paths on the working directory (annoying)
            p.StartInfo.WorkingDirectory = Path.GetDirectoryName(command[0]);
            p.Start();
        }
    }

    class Game
    {
        public string name;
        public GamesNode gamesNode;
        public IEnumerable<ROM> roms;

        public Game(string name, GamesNode gamesNode, IEnumerable<ROM> roms)
        {
            this.name = name;
            this.gamesNode = gamesNode;
            this.roms = roms;
        }

        public ROM DefaultROM()
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

        public bool MatchesNameFilter(string nameFilter)
        {
            // TODO match against a version with no punctuation?

            return nameFilter.Length == 0 || name.Contains(nameFilter, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    abstract class Node
    {
        public readonly string name;

        public abstract bool Enabled { get; set; }
        public abstract double Weight { get; set; }
        public abstract double EffectiveWeight { get; }

        public abstract IEnumerable<Game> Games { get; }
        public IEnumerable<Game> FilteredGames => Games.Where(game => game.MatchesNameFilter(Program.gameNameFilter));
        public bool AnyGamesMatchFilter => Games.Any(game => game.MatchesNameFilter(Program.gameNameFilter));

        public Node(string name)
        {
            this.name = name;
        }

        public abstract SavedConfig.Node ToSavedConfigNode();
    }

    class GamesNode : Node
    {
        private readonly string dirPath;
        private readonly List<Game> games;

        public override bool Enabled { get; set; }
        public override double Weight { get; set; }
        public override double EffectiveWeight => (Enabled && AnyGamesMatchFilter) ? Weight : 0;

        public override IEnumerable<Game> Games => games;

        public GamesNode(SavedConfig.Node savedConfigNode)
            : base(savedConfigNode.Name)
        {
            Debug.Assert(savedConfigNode.DirPath != null);
            Debug.Assert(savedConfigNode.ChildNodes.Count == 0);

            dirPath = savedConfigNode.DirPath;

            if (Directory.Exists(dirPath))
            {
                games = Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories)
                    .Where(filePath => !ROMNameParser.IsBios(filePath))
                    .Select(filePath => new ROM(filePath, name))
                    .Where(rom => rom.CanPlay)
                    .GroupBy(rom => rom.details.name)
                    .Select(grouping => new Game(grouping.Key, this, grouping))
                    .ToList();
            }
            else
            {
                games = new List<Game>();
            }

            Enabled = savedConfigNode.Enabled;
            Weight = savedConfigNode.Weight;

            if (Weight == 0)
            {
                Weight = games.Count;
            }
        }

        public override SavedConfig.Node ToSavedConfigNode()
        {
            SavedConfig.Node node = new SavedConfig.Node();
            node.Name = name;
            node.Enabled = Enabled;
            node.Weight = Weight;
            node.DirPath = dirPath;
            return node;
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

        public GroupNode(SavedConfig.Node savedConfigNode)
            : base(savedConfigNode.Name)
        {
            Debug.Assert(savedConfigNode.DirPath == null);
            Debug.Assert(savedConfigNode.ChildNodes.Count != 0);

            subNodes = new List<Node>();

            foreach (SavedConfig.Node childNode in savedConfigNode.ChildNodes)
            {
                if (childNode.DirPath == null)
                {
                    subNodes.Add(new GroupNode(childNode));
                }
                else
                {
                    subNodes.Add(new GamesNode(childNode));
                }
            }
        }

        public override SavedConfig.Node ToSavedConfigNode()
        {
            SavedConfig.Node node = new SavedConfig.Node();
            node.Name = name;
            node.ChildNodes = subNodes.Select(subNode => subNode.ToSavedConfigNode()).ToList();
            return node;
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
                        return games[Random.Shared.Next(games.Count - 1)];
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
        public ROM? rom;

        public Game? Game
        {
            get { return game; }
            set { game = value; rom = (game == null) ? null : game.DefaultROM(); }
        }

        public Reel(Game? game)
        {
            this.spinning = true;
            this.Game = game;
        }
    }

    class Program
    {
        static ImFontPtr font20;
        static ImFontPtr font30;
        static ImFontPtr font40;

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

        static void Main(string[] args)
        {
            {
                string jsonSavedConfig = File.ReadAllText("rr_config.txt");
                SavedConfig savedConfig = JsonSerializer.Deserialize<SavedConfig>(jsonSavedConfig);
                rootNode = new GroupNode(savedConfig.RootNode);
            }

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

                cl.Begin();
                cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
                cl.ClearColorTarget(0, new RgbaFloat(1, 1, 1, 1));
                controller.Render(gd, cl);
                cl.End();
                gd.SubmitCommands(cl);
                gd.SwapBuffers(gd.MainSwapchain);
            }

            {
                SavedConfig savedConfig = new SavedConfig();
                savedConfig.RootNode = rootNode.ToSavedConfigNode();
                string jsonSavedConfig = JsonSerializer.Serialize(savedConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText("rr_config.txt", jsonSavedConfig);
            }

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
                    if (ImGui.BeginTabItem("Browser"))
                    {
                        RenderBrowserTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Roulette"))
                    {
                        RenderRouletteTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Config"))
                    {
                        RenderConfigTab();
                        ImGui.EndTabItem();
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

            List<Game> matchingGames = rootNode.Games.Where(game => game.MatchesNameFilter(browserSearch)).ToList(); // TODO this is using the filter!

            ImGui.SameLine();
            ImGui.Text($"{matchingGames.Count}");

            if (ImGui.BeginTable("matches", 2, ImGuiTableFlags.ScrollY)) // TODO first col width
            {
                const int maxDisplayed = 100;

                foreach (Game game in matchingGames.Take(maxDisplayed)) // TODO allow more
                {
                    foreach (ROM rom in game.roms)
                    {
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();

                        ImGui.TextUnformatted(game.gamesNode.name);

                        ImGui.TableNextColumn();

                        if (ImGui.Selectable(Path.GetFileName(rom.path)))
                            rom.Play();
                    }
                }

                ImGui.EndTable();
            }

            // TODO "copy to roulette"
        }

        private static void RenderRouletteTab()
        {
            bool showGameSelCfgThisFrame = showGameSelCfg;

            if (showGameSelCfgThisFrame)
            {
                ImGui.BeginChild("roulette", new Vector2(ImGui.GetContentRegionAvail().X / 2, ImGui.GetContentRegionAvail().Y), ImGuiChildFlags.ResizeX);
            }

            Vector2 rouletteTopRight = ImGui.GetCursorPos() + new Vector2(ImGui.GetContentRegionAvail().X, 0);

            RenderRoulette();

            // Show/hide selection config button
            {
                Vector2 cursorPosBefore = ImGui.GetCursorPos();

                ImGui.SetCursorPos(rouletteTopRight + new Vector2(-50, 0));

                if (ImGui.Button(showGameSelCfgThisFrame ? ">" : "<", new Vector2(50, 50)))
                {
                    showGameSelCfg = !showGameSelCfg;
                }

                ImGui.SetCursorPos(cursorPosBefore);
            }

            if (showGameSelCfgThisFrame)
            {
                ImGui.EndChild();

                ImGui.SameLine(0, 0);

                ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.07f, 0.07f, 0.07f, 1.0f));

                if (ImGui.BeginChild("selectionconfig", ImGui.GetContentRegionAvail()))
                {
                    RenderGameSelectionConfig();
                }

                ImGui.PopStyleColor();

                ImGui.EndChild();
            }
        }

        private static void RenderRoulette()
        {
            ImGui.PushFont(font40);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding * 2);
            ImGui.BeginDisabled(reels.Any(reel => reel.spinning));

            float buttonsSize = ImGui.CalcTextSize("Spin").X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - buttonsSize / 2));
            if (ImGui.Button("Spin"))
            {
                reels.Clear();

                for (int i = 0; i < nReels; i++)
                {
                    reels.Add(new Reel(rootNode.WeightedFilteredRandomGame()));
                }

                nextSpinTick = appStopwatch.Elapsed.TotalSeconds;
            }

            ImGui.EndDisabled();
            ImGui.PopStyleVar();
            ImGui.PopFont();

            ImGui.SameLine();

            ImGui.SetNextItemWidth(80);

            if (ImGui.InputInt("##nReels", ref nReels))
            {
                if (nReels <= 1)
                    nReels = 1;

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

            const float searchWidth = 300;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - searchWidth / 2));
            ImGui.SetNextItemWidth(searchWidth);
            ImGui.InputText("##search", ref gameNameFilter, 128);
            ImGui.SameLine();
            ImGui.Text($"{rootNode.FilteredGames.Count()}");

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

            const int stopColWidth = 90;
            const int nameColWidth = 400;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - nameColWidth / 2) - stopColWidth);

            if (ImGui.BeginTable("roms", 3))
            {
                ImGui.TableSetupColumn("##stop", ImGuiTableColumnFlags.WidthFixed, stopColWidth);
                ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, nameColWidth);
                ImGui.TableSetupColumn("##button", ImGuiTableColumnFlags.WidthFixed, 300);

                int i = 0;

                foreach (Reel reel in reels)
                {
                    i++;

                    Game? game = reel.Game;

                    ImGui.TableNextRow();

                    if (game != null)
                    {
                        ImGui.TableNextColumn();

                        if (reel.spinning)
                        {
                            Vector4 color = new Vector4(0.9f, 0.3f, 0.2f, 1.0f);
                            ImGui.PushStyleColor(ImGuiCol.Button, color);
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * 0.9f);
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * 0.7f);

                            if (ImGui.Button($"##stop{i}", new Vector2(60, 60)))
                            {
                                reel.spinning = false;
                            }

                            ImGui.PopStyleColor(3);
                        }

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
                        ImGui.Text(game.gamesNode.name);

                        ImGui.Spacing();
                        ImGui.Spacing();

                        ImGui.TableNextColumn();

                        if (!reel.spinning)
                        {
                            ImGui.BeginDisabled(game.roms.Count() == 1);

                            if (ImGui.BeginCombo($"##options{game.gamesNode.name}+{game.name}", reel.rom!.details.PropsString()))
                            {
                                foreach (ROM romSelectable in game.roms)
                                {
                                    if (ImGui.Selectable(romSelectable.details.PropsString()))
                                    {
                                        reel.rom = romSelectable;
                                    }
                                }

                                ImGui.EndCombo();
                            }

                            ImGui.EndDisabled();

                            if (ImGui.Button($"Play##{game.gamesNode}+{game.name}"))
                                reel.rom.Play();
                        }
                    }
                    else
                    {
                        ImGui.TableNextColumn();

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
                            if (ImGui.TreeNode(groupNode.name))
                            {
                                nodeOpen = true;
                                foldersStack.Push(new Queue<Node>(groupNode.subNodes));
                            }
                        }

                        ImGui.TableNextColumn();

                        bool enabled = nextNode.Enabled;
                        if (ImGui.Checkbox($"##{nextNode.name}", ref enabled))
                        {
                            nextNode.Enabled = enabled;
                        }

                        ImGui.TableNextColumn();

                        if (nodeOpen) // TODO maybe hiding was the right thing instead of this?
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

        static SavedConfig.Node? savedconfigRootNodePendingSave = null; // TODO disable other tabs if true?

        private static void RenderConfigTab()
        {
            if (ImGui.BeginTable("nodetree", 2))
            {
                //ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.IndentDisable, 25);
                //ImGui.TableSetupColumn("##up", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.IndentDisable, 25);
                //ImGui.TableSetupColumn("##down", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.IndentDisable, 25);
                ImGui.TableSetupColumn("name", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.IndentEnable, 250);
                ImGui.TableSetupColumn("props", ImGuiTableColumnFlags.WidthStretch);

                SavedConfig.Node savedconfigRootNodeRender = savedconfigRootNodePendingSave ?? rootNode.ToSavedConfigNode();

                Stack<Queue<SavedConfig.Node>> foldersStack = new Stack<Queue<SavedConfig.Node>>();
                foldersStack.Push(new Queue<SavedConfig.Node>(savedconfigRootNodeRender.ChildNodes));

                while (foldersStack.Count > 0)
                {
                    if (foldersStack.Peek().TryDequeue(out SavedConfig.Node nextNode))
                    {
                        ImGui.TableNextRow();

                        //float btnHeight = ImGui.CalcTextSize("^").Y + ImGui.GetStyle().FramePadding.Y * 2;

                        //ImGui.TableNextColumn();

                        //ImGui.Button("-", new Vector2(btnHeight, 0));

                        //ImGui.TableNextColumn();

                        //ImGui.Button("^", new Vector2(btnHeight, 0));

                        //ImGui.TableNextColumn();

                        //ImGui.Button("v", new Vector2(btnHeight, 0));

                        if (nextNode.ChildNodes.Count == 0)
                        {
                            ImGui.TableNextColumn();
                            ImGui.TreeNodeEx(nextNode.Name, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);

                            ImGui.TableNextColumn();

                            string dirPathEdit = nextNode.DirPath!;
                            ImGui.InputText($"##folderpath_{nextNode.Name}", ref dirPathEdit, 512);

                            if (ImGui.IsItemEdited())
                            {
                                nextNode.DirPath = dirPathEdit;

                                if (savedconfigRootNodePendingSave == null)
                                {
                                    savedconfigRootNodePendingSave = savedconfigRootNodeRender;
                                }
                            }
                        }
                        else
                        {
                            ImGui.TableNextColumn();

                            if (ImGui.TreeNode(nextNode.Name))
                            {
                                foldersStack.Push(new Queue<SavedConfig.Node>(nextNode.ChildNodes));
                            }

                            ImGui.TableNextColumn();
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

            ImGui.BeginDisabled(savedconfigRootNodePendingSave == null);

            bool saveHotkeyPressed = (ImGui.IsKeyDown(ImGuiKey.ModCtrl) && ImGui.IsKeyReleased(ImGuiKey.S));

            if (ImGui.Button("Save") || (savedconfigRootNodePendingSave != null && saveHotkeyPressed))
            {
                rootNode = new GroupNode(savedconfigRootNodePendingSave);
                savedconfigRootNodePendingSave = null;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                savedconfigRootNodePendingSave = null;
            }

            ImGui.EndDisabled();
        }
    }
}