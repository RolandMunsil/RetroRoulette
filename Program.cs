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
        private string folderName; // TODO remove?

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
                // TODO: Game Master, GP32, Playstation Portable, Pokemon Mini, Tiger Game.com, Watara Supervision
                // TODO: LeapFrog Leapster? VTech V.Smile
                // TODO: Amiga CD32
                // TODO: Commodore CDTV, Funtech Super A'Can
                // TODO: Atari 7800, Philips Videopac+ G7400, Sega SG-1000, Super Casette Vision
                // TODO: 2nd Generation
                // TODO: Home Computers

                // TODO: Ooh old windows 98/95 games

                // TODO test all the systems
                // TODO list roms that weren't playable
                // TODO merge ROMs by name?

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
                or ("Sega Saturn" or "PlayStation", ".cue" or ".chd")
                    => new[] { "C:\\Portable Programs\\Mednafen & Mednaffe\\mednafen.exe", path },
                ("Game Gear", ".sms" or ".gg")
                    => new[] { "C:\\Portable Programs\\ares-v132\\ares.exe", "--system", "Game Gear", path },
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
                // TODO: these take waaaaay too long to load. maybe a dedicated emulator will fix it?
                //("ZX Spectrum", _) // dump, quik, cass
                //    => new[] { "C:\\Portable Programs\\MAME\\mame.exe", "spec128", "-cass", path },
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
        public List<Game> games = new List<Game>();


        public bool IsGameDir => subfolders.Count == 0;
        public IEnumerable<Game> AllGames => games.Concat(subfolders.SelectMany(folder => folder.AllGames));
        public int CombinedWeight => IsGameDir ? Weight : subfolders.Sum(folder => folder.CombinedWeight);

        public bool enabled;
        private int weightInternal;
        public int Weight => enabled ? weightInternal : 0;

        public FolderNode(string dirPath)
        {
            this.name = dirPath.Split(Path.DirectorySeparatorChar).Last();
            Populate(dirPath);

            enabled = true;
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
    }

    class Reel
    {
        public bool spinning;
        public Game game;
        public ROM rom;

        public Reel(Game game)
        {
            this.spinning = true;
            this.game = game;
            this.rom = game.DefaultROM();
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

        static string gameNameFilter = "";
        static bool MatchesFilter(string str) => gameNameFilter.Length == 0 || str.Contains(gameNameFilter, StringComparison.CurrentCultureIgnoreCase);

        static FolderNode rootNode = new FolderNode("D:\\ROMs");

        static bool showAdvanced = false;

        static void Main(string[] args)
        {
           Sdl2Window window;
           GraphicsDevice gd;

            // Create window, GraphicsDevice, and all resources necessary for the demo.

            VeldridStartup.CreateWindowAndGraphicsDevice(
                new WindowCreateInfo(50, 50, 1280, 720, WindowState.Normal, "ImGui.NET Sample Program"),
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

            List<Game> possibleGames = rootNode.AllGames.Where(game => MatchesFilter(game.name)).ToList();
            Func<Game> fnRandGame = () => possibleGames[Random.Shared.Next(possibleGames.Count() - 1)];

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
                        reels.Add(new Reel(fnRandGame()));
                    }

                    nextSpinTick = appStopwatch.Elapsed.TotalSeconds;
                }

                ImGui.EndDisabled();
                ImGui.PopStyleVar();
                ImGui.PopFont();

                ImGui.SameLine();

                ImGui.SetNextItemWidth(80);
                ImGui.InputInt("##a", ref nReels);

                //if (ImGui.Button("-"))
                //    nReels--;
                //ImGui.SameLine();
                //ImGui.Text($"{nReels}");
                //ImGui.SameLine();
                //if (ImGui.Button("+"))
                //    nReels++;
            }

            const float searchWidth = 300;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - searchWidth / 2));
            ImGui.SetNextItemWidth(searchWidth);
            ImGui.InputText("##search", ref gameNameFilter, 128);
            ImGui.SameLine();
            ImGui.Text($"{possibleGames.Count}");

            if (appStopwatch.Elapsed.TotalSeconds >= nextSpinTick)
            {
                for (int i = 0; i < reels.Count; i++)
                {
                    if (reels[i].spinning)
                    {
                        reels[i].game = fnRandGame();
                        reels[i].rom = reels[i].game.DefaultROM();
                    }
                }

                if (reels.Any(reel => reel.spinning))
                {
                    nextSpinTick = appStopwatch.Elapsed.TotalSeconds + spinTickTime;
                }
            }

            const int stopColWidth = 90;
            const int nameColWidth = 400;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - nameColWidth / 2) - stopColWidth);

            if (ImGui.BeginTable("roms", 3))
            {
                ImGui.TableSetupColumn("##stop", ImGuiTableColumnFlags.WidthFixed, stopColWidth);
                ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, nameColWidth);
                ImGui.TableSetupColumn("##button", ImGuiTableColumnFlags.WidthFixed, 300);

                for (int i = 0; i < reels.Count; i++)
                {
                    Reel reel = reels[i];

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();

                    if (reel.spinning)
                    {
                        ImGui.PushFont(font40);

                        if (ImGui.Button("Stop"))
                        {
                            reel.spinning = false;
                        }

                        ImGui.PopFont();
                    }

                    ImGui.TableNextColumn();
                    ImGui.PushFont(font30);
                    if (reel.spinning)
                    {
                        ImGui.Text(reel.game.name);
                    }
                    else
                    {
                        ImGui.TextWrapped(reel.game.name);
                    }
                    ImGui.PopFont();
                    ImGui.Text(reel.game.folder.name);

                    ImGui.Spacing();
                    ImGui.Spacing();

                    ImGui.TableNextColumn();

                    if (!reel.spinning)
                    {
                        ImGui.BeginDisabled(reel.game.roms.Count() == 1);

                        if (ImGui.BeginCombo($"##options{reel.game.folder}+{reel.game.name}", reel.rom.details.PropsString()))
                        {
                            foreach (ROM romSelectable in reel.game.roms)
                            {
                                if (ImGui.Selectable(romSelectable.details.PropsString()))
                                {
                                    reel.rom = romSelectable;
                                }
                            }

                            ImGui.EndCombo();
                        }

                        ImGui.EndDisabled();

                        if (ImGui.Button($"Play##{reel.game.folder}+{reel.game.name}"))
                            reel.rom.Play();
                    }
                }

                ImGui.EndTable();
            }

            // Render folder browser

            ImGui.SetCursorPosY(380);

            ImGui.Checkbox("Advanced", ref showAdvanced);

            if (showAdvanced)
            {
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.BeginTable("table", 3))
                {
                    ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, 250);
                    ImGui.TableSetupColumn("##enable", ImGuiTableColumnFlags.WidthFixed, 25);
                    ImGui.TableSetupColumn("##bar", ImGuiTableColumnFlags.WidthStretch);

                    int weightTotal = rootNode.CombinedWeight;

                    Stack<Queue<FolderNode>> foldersStack = new Stack<Queue<FolderNode>>();
                    foldersStack.Push(new Queue<FolderNode>(rootNode.subfolders));

                    while (foldersStack.Count > 0)
                    {
                        if (foldersStack.Peek().TryDequeue(out FolderNode nextNode))
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
                                        foreach (Game game in nextNode.games)
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

                            ImGui.Checkbox($"##{nextNode.name}", ref nextNode.enabled);

                            ImGui.TableNextColumn();

                            if (!nodeOpen)
                            {
                                //ImGui.PushStyleColor(ImGuiCol.PlotHistogram)

                                float weightFraction = nextNode.CombinedWeight / (float)weightTotal;

                                string display = $"{100 * weightFraction:f1}% ({nextNode.AllGames.Count()} games)";

                                ImGui.ProgressBar(weightFraction, new Vector2(-50, 0f), nextNode.enabled ? display : "(disabled)");
                            }
                        }
                        else
                        {
                            while (foldersStack.Count > 0 && foldersStack.Peek().Count == 0)
                            {
                                foldersStack.Pop();
                                ImGui.TreePop();
                            }
                        }
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.ShowDemoWindow();

            ImGui.End();
        }
    }
}