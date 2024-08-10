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
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;
using Vulkan.Xlib;

namespace RetroRoulette
{
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
                // - tighten up weight adjustment UI. Maybe just need to make it less wide?
                // - list roms that weren't playable
                // - save weights to file?
                // - allow adjusting weights of non-open nodes, just indicate they're different somehow.
                // - option to hide names but not systems (in reels)
                // - filters for additional flags on ROMs, and regions. E.g. only play unlicensed games, or only play Korean games
                // - some way of creating custom tags/collections? would be cool to do like a "Classics of Game" night
                // - support using no-intro, redump, etc xmls to get more info about ROMs?

                ("Gamecube" or "Wii (Discs)" or "Wii (WiiWare)", ".rvz" or ".wbfs" or ".wad")
                    => new[] { "C:\\Portable Programs\\Dolphin\\Dolphin.exe", "-e", path },
                ("Playstation 2", ".bin" or ".iso")
                    => new[] { "C:\\Program Files (x86)\\PCSX2\\pcsx2.exe", path },
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
        public FolderNode folder;
        public IEnumerable<ROM> roms;

        public Game(string name, FolderNode folder, IEnumerable<ROM> roms)
        {
            this.name = name;
            this.folder = folder;
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
    }

    class FolderNode
    {
        public readonly string name;
        public List<FolderNode> subfolders = new List<FolderNode>();
        private List<Game> games = new List<Game>();

        public static string gameNameFilter = "";
        public static bool MatchesFilter(string str) => gameNameFilter.Length == 0 || str.Contains(gameNameFilter, StringComparison.CurrentCultureIgnoreCase);
        public IEnumerable<Game> FilteredGames => Enabled ? games.Where(game => MatchesFilter(game.name)) : Enumerable.Empty<Game>();

        public bool IsGameDir => subfolders.Count == 0;
        public IEnumerable<Game> AllGames => FilteredGames.Concat(subfolders.SelectMany(folder => folder.AllGames));
        public double CombinedWeight => IsGameDir ? Weight : subfolders.Sum(folder => folder.CombinedWeight);

        private bool enabledInternal;
        public bool Enabled
        {
            get
            {
                return IsGameDir ? enabledInternal : subfolders.Any(folder => folder.Enabled);
            }
            set
            {
                if (IsGameDir)
                {
                    enabledInternal = value;
                }
                else
                {
                    subfolders.ForEach(subfolder => subfolder.Enabled = value);
                }
            }
        }

        private double weightInternal;
        public double Weight => FilteredGames.Any() ? weightInternal : 0;

        public FolderNode(string dirPath)
        {
            this.name = dirPath.Split(Path.DirectorySeparatorChar).Last();
            Populate(dirPath);

            enabledInternal = true;
            weightInternal = games.Count;
        }

        private void Populate(string dirPath)
        {
            if (File.Exists(Path.Combine(dirPath, "_noroms_tree.rrt")))
                return;

            if (File.Exists(Path.Combine(dirPath, "_noroms.rrt")))
            {
                subfolders = Directory.GetDirectories(dirPath)
                    .Select(dir => new FolderNode(dir))
                    .Where(node => node.subfolders.Count > 0 || node.games.Count > 0).ToList();
            }
            else
            {
                games = Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories)
                    .Where(filePath => !ROMNameParser.IsBios(filePath))
                    .Select(filePath => new ROM(filePath, name))
                    .Where(rom => rom.CanPlay)
                    .GroupBy(rom => rom.details.name)
                    .Select(grouping => new Game(grouping.Key, this, grouping))
                    .ToList();
            }
        }

        public Game? WeightedRandomGame()
        {
            if (IsGameDir)
            {
                List<Game> filteredGames = FilteredGames.ToList();
                return filteredGames[Random.Shared.Next(filteredGames.Count - 1)];
            }

            double next = Random.Shared.NextDouble() * CombinedWeight;
            double total = 0;

            foreach (FolderNode node in subfolders)
            {
                total += node.CombinedWeight;

                if (next < total)
                {
                    return node.WeightedRandomGame();
                }
            }

            return null;
        }

        public void MultiplyWeight(double mul)
        {
            if (IsGameDir)
            {
                weightInternal *= mul;
            }
            else
            {
                subfolders.ForEach(folder => folder.MultiplyWeight(mul));
            }
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

        static FolderNode rootNode = new FolderNode("D:\\ROMs");

        static bool showConfig = false;
        static bool showFinder = false;

        static FolderNode? nodeDraggedProgressBar = null;

        static void Main(string[] args)
        {
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

                UIUpdate();

                cl.Begin();
                cl.SetFramebuffer(gd.MainSwapchain.Framebuffer);
                cl.ClearColorTarget(0, new RgbaFloat(1, 1, 1, 1));
                controller.Render(gd, cl);
                cl.End();
                gd.SubmitCommands(cl);
                gd.SwapBuffers(gd.MainSwapchain);
            }

            // Clean up Veldrid resources
            gd.WaitForIdle();
            controller.Dispose();
            cl.Dispose();
            gd.Dispose();
        }

        static void UIUpdate()
        {
            ImGuiViewportPtr viewportptr = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewportptr.WorkPos);
            ImGui.SetNextWindowSize(viewportptr.WorkSize);
            ImGui.SetNextWindowViewport(viewportptr.ID);

            ImGui.Begin("Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

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
                        reels.Add(new Reel(rootNode.WeightedRandomGame()));
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
                            reels.Add(new Reel(rootNode.WeightedRandomGame()));
                        }

                        while (nReels < reels.Count && reels.Last().spinning)
                        {
                            reels.RemoveAt(reels.Count - 1);
                        }
                    }
                }
            }

            const float searchWidth = 300;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - searchWidth / 2));
            ImGui.SetNextItemWidth(searchWidth);
            ImGui.InputText("##search", ref FolderNode.gameNameFilter, 128);
            ImGui.SameLine();
            ImGui.Text($"{rootNode.AllGames.Count()}");

            if (reels.Any(reel => reel.spinning) && appStopwatch.Elapsed.TotalSeconds >= nextSpinTick)
            {
                foreach (Reel reel in reels)
                {
                    if (reel.spinning)
                    {
                        reel.Game = rootNode.WeightedRandomGame();
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
                        ImGui.Text(game.folder.name);

                        ImGui.Spacing();
                        ImGui.Spacing();

                        ImGui.TableNextColumn();

                        if (!reel.spinning)
                        {
                            ImGui.BeginDisabled(game.roms.Count() == 1);

                            if (ImGui.BeginCombo($"##options{game.folder}+{game.name}", reel.rom!.details.PropsString()))
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

                            if (ImGui.Button($"Play##{game.folder}+{game.name}"))
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

            // Render folder browser

            ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY(), 380));
            ImGui.Checkbox("Show config", ref showConfig);
            ImGui.Checkbox("Show matching games list", ref showFinder);

            if (showConfig)
            {
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.BeginTable("table", 3))
                {
                    ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, 250);
                    ImGui.TableSetupColumn("##enable", ImGuiTableColumnFlags.WidthFixed, 25);
                    ImGui.TableSetupColumn("##bar", ImGuiTableColumnFlags.WidthStretch);

                    double weightTotal = rootNode.CombinedWeight;

                    Stack<Queue<FolderNode>> foldersStack = new Stack<Queue<FolderNode>>();
                    foldersStack.Push(new Queue<FolderNode>(rootNode.subfolders));

                    while (foldersStack.Count > 0)
                    {
                        if (foldersStack.Peek().TryDequeue(out FolderNode? nextNode))
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();

                            bool nodeOpen = false;

                            if (nextNode.IsGameDir)
                            {
                                if (ImGui.IsKeyDown(ImGuiKey.ModCtrl))
                                {
                                    if (ImGui.TreeNode(nextNode.name))
                                    {
                                        foreach (Game game in nextNode.FilteredGames)
                                        {
                                            if (ImGui.TreeNode($"{game.name}"))
                                            {
                                                foreach (ROM rom in game.roms)
                                                {
                                                    if (ImGui.Selectable(Path.GetFileName(rom.path)))
                                                        rom.Play();
                                                }

                                                ImGui.TreePop();
                                            }
                                        }

                                        ImGui.TreePop();
                                    }
                                }
                                else
                                {
                                    ImGui.TreeNodeEx(nextNode.name, ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
                                }
                            }
                            else
                            {
                                if (ImGui.TreeNode(nextNode.name))
                                {
                                    nodeOpen = true;
                                    foldersStack.Push(new Queue<FolderNode>(nextNode.subfolders));
                                }
                            }

                            ImGui.TableNextColumn();

                            bool enabled = nextNode.Enabled;
                            if (ImGui.Checkbox($"##{nextNode.name}", ref enabled))
                            {
                                nextNode.Enabled = enabled;
                            }

                            ImGui.TableNextColumn();

                            if (!nodeOpen)
                            {
                                //ImGui.PushStyleColor(ImGuiCol.PlotHistogram)

                                double weightFraction = nextNode.CombinedWeight / weightTotal;

                                string display = $"{100 * weightFraction:f1}% ({nextNode.AllGames.Count()} games)";

                                ImGui.ProgressBar((float)weightFraction, new Vector2(-10, 0f), nextNode.Enabled ? display : "(disabled)");

                                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                                    nodeDraggedProgressBar = nextNode;

                                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                                    nodeDraggedProgressBar = null;

                                if (nodeDraggedProgressBar == nextNode)
                                    nextNode.MultiplyWeight(Math.Pow(1.01, ImGui.GetIO().MouseDelta.X));
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

            if (showFinder)
            {
                ImGui.Separator();
                ImGui.Spacing();

                if (!String.IsNullOrEmpty(FolderNode.gameNameFilter))
                {
                    foreach (Game game in rootNode.AllGames.Take(100))
                    {
                        foreach (ROM rom in game.roms)
                        {
                            if (ImGui.Selectable($"{Path.GetFileName(rom.path)} [{game.folder.name}]"))
                                rom.Play();
                        }
                    }
                }
            }

            ImGui.End();
        }
    }
}