﻿using ImGuiNET;
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
    class Program
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
                if (usaRoms.Count > 1)
                {
                    filteredRoms = usaRoms;
                }

                List<ROM> noPropsRoms = filteredRoms.Where(rom => rom.details.additionalProperties.Count == 0).ToList();
                if (noPropsRoms.Count > 1)
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
            // public FolderNode parent;

            public bool enabled = true;

            public bool IsRomDir => subfolders.Count == 0;
            public IEnumerable<Game> AllGames => games.Concat(subfolders.SelectMany(folder => folder.AllGames));

            public FolderNode(string dirPath)
            {
                this.name = dirPath.Split(Path.DirectorySeparatorChar).Last();
                Populate(dirPath);
            }

            private void Populate(string dirPath)
            {
                if (File.Exists(Path.Combine(dirPath, "_noroms_tree.rrt")))
                    return;

                if (File.Exists(Path.Combine(dirPath, "_noroms.rrt")))
                {
                    subfolders = Directory.GetDirectories(dirPath).Select(dir => new FolderNode(dir)).Where(node => node.subfolders.Count > 0 || node.games.Count > 0).ToList();
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


        static List<Reel> reels = new List<Reel>();

        static string search = "";
        static bool MatchesSearch(string str) => search.Length == 0 || str.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        static bool showAdvanced = false;
        static int nGames = 3;

        static double spinTickTime = 0.04f;
        static double nextSpinTick = 0;

        static void CenterNextItem(float itemWidth)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - itemWidth / 2));
            ImGui.SetNextItemWidth(itemWidth);
        }

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

            CommandList cl = gd.ResourceFactory.CreateCommandList();
            ImGuiController controller = new ImGuiController(gd, gd.MainSwapchain.Framebuffer.OutputDescription, window.Width, window.Height);

            window.Resized += () =>
            {
                gd.MainSwapchain.Resize((uint)window.Width, (uint)window.Height);
                controller.WindowResized(window.Width, window.Height);
            };


            FolderNode rootNode = new FolderNode("D:\\ROMs");

            Stopwatch stopwatch = Stopwatch.StartNew();
            Stopwatch swLifetime = Stopwatch.StartNew();

            // Main application loop
            while (window.Exists)
            {
                float deltaTime = stopwatch.ElapsedTicks / (float)Stopwatch.Frequency;
                stopwatch.Restart();
                InputSnapshot snapshot = window.PumpEvents();
                if (!window.Exists) { break; }
                controller.Update(deltaTime, snapshot); // Feed the input events to our ImGui controller, which passes them through to ImGui.

                //SubmitUI();

                ImGuiViewportPtr viewportptr = ImGui.GetMainViewport();
                ImGui.SetNextWindowPos(viewportptr.WorkPos);
                ImGui.SetNextWindowSize(viewportptr.WorkSize);
                ImGui.SetNextWindowViewport(viewportptr.ID);

                ImGui.Begin("Main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove);

                List<Game> possibleGames = rootNode.AllGames.Where(game => MatchesSearch(game.name)).ToList();
                Func<Game> fnRandGame = () => possibleGames[Random.Shared.Next(possibleGames.Count() - 1)];

                {
                    ImGui.PushFont(controller.font40);
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding * 2);
                    ImGui.BeginDisabled(reels.Any(reel => reel.spinning));
                    
                    float buttonsSize = ImGui.CalcTextSize("Spin").X + ImGui.GetStyle().FramePadding.X * 2;
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X / 2 - buttonsSize / 2));
                    if (ImGui.Button("Spin"))
                    {
                        reels.Clear();

                        for (int i = 0; i < nGames; i++)
                        {
                            reels.Add(new Reel(fnRandGame()));
                        }

                        nextSpinTick = swLifetime.Elapsed.TotalSeconds;
                    }

                    ImGui.EndDisabled();
                    ImGui.PopStyleVar();
                    ImGui.PopFont();

                    ImGui.SameLine();

                    if (ImGui.Button("-"))
                        nGames--;
                    ImGui.SameLine();
                    ImGui.Text($"{nGames}");
                    ImGui.SameLine();
                    if (ImGui.Button("+"))
                        nGames++;
                }

                CenterNextItem(300);
                ImGui.InputText("##search", ref search, 128);
                ImGui.SameLine();
                ImGui.Text($"{possibleGames.Count}");

                if (swLifetime.Elapsed.TotalSeconds >= nextSpinTick)
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
                        nextSpinTick = swLifetime.Elapsed.TotalSeconds + spinTickTime;
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
                            ImGui.PushFont(controller.font40);

                            if (ImGui.Button("Stop"))
                            {
                                reel.spinning = false;
                            }

                            ImGui.PopFont();
                        }

                        ImGui.TableNextColumn();
                        ImGui.PushFont(controller.font30);
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

                ImGui.SetCursorPosY(350);

                ImGui.Checkbox("Advanced", ref showAdvanced);

                if (showAdvanced)
                {
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    if (ImGui.BeginTable("table", 3))
                    {
                        ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, 500);
                        ImGui.TableSetupColumn("##enable", ImGuiTableColumnFlags.WidthFixed, 25);
                        ImGui.TableSetupColumn("##count", ImGuiTableColumnFlags.WidthFixed, 100);

                        Stack<Queue<FolderNode>> foldersStack = new Stack<Queue<FolderNode>>();
                        foldersStack.Push(new Queue<FolderNode>(rootNode.subfolders));

                        while (foldersStack.Count > 0)
                        {
                            if (foldersStack.Peek().TryDequeue(out FolderNode nextNode))
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();

                                if (ImGui.TreeNode(nextNode.name))
                                {
                                    if (nextNode.IsRomDir)
                                    {
                                        foreach (Game game in nextNode.games)
                                        {
                                            if (MatchesSearch(game.name) && ImGui.TreeNode($"{game.name}"))
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
                                    else
                                    {
                                        foldersStack.Push(new Queue<FolderNode>(nextNode.subfolders));
                                    }
                                }

                                ImGui.TableNextColumn();

                                ImGui.Checkbox($"##{nextNode.name}", ref nextNode.enabled);

                                ImGui.TableNextColumn();

                                ImGui.PushStyleColor(ImGuiCol.Text, 0xFF00FF00);
                                ImGui.Text($"{nextNode.AllGames.Count()}");
                                ImGui.PopStyleColor();
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


                ImGui.End();

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
    }


    /// <summary>
    /// A modified version of Veldrid.ImGui's ImGuiRenderer.
    /// Manages input for ImGui and handles rendering ImGui's DrawLists with Veldrid.
    /// </summary>
    public class ImGuiController : IDisposable
    {
        private GraphicsDevice _gd;
        private bool _frameBegun;

        // Veldrid objects
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private DeviceBuffer _projMatrixBuffer;
        private Texture _fontTexture;
        private TextureView _fontTextureView;
        private Shader _vertexShader;
        private Shader _fragmentShader;
        private ResourceLayout _layout;
        private ResourceLayout _textureLayout;
        private Pipeline _pipeline;
        private ResourceSet _mainResourceSet;
        private ResourceSet _fontTextureResourceSet;

        private IntPtr _fontAtlasID = (IntPtr)1;

        private int _windowWidth;
        private int _windowHeight;
        
        public ImFontPtr font20;
        public ImFontPtr font30;
        public ImFontPtr font40;

        private struct ResourceSetInfo
        {
            public readonly IntPtr ImGuiBinding;
            public readonly ResourceSet ResourceSet;

            public ResourceSetInfo(IntPtr imGuiBinding, ResourceSet resourceSet)
            {
                ImGuiBinding = imGuiBinding;
                ResourceSet = resourceSet;
            }
        }

        // Image trackers
        private readonly Dictionary<TextureView, ResourceSetInfo> _setsByView
            = new Dictionary<TextureView, ResourceSetInfo>();
        private readonly Dictionary<Texture, TextureView> _autoViewsByTexture
            = new Dictionary<Texture, TextureView>();
        private readonly Dictionary<IntPtr, ResourceSetInfo> _viewsById = new Dictionary<IntPtr, ResourceSetInfo>();
        private readonly List<IDisposable> _ownedResources = new List<IDisposable>();
        private int _lastAssignedID = 100;

        /// <summary>
        /// Constructs a new ImGuiController.
        /// </summary>
        public ImGuiController(GraphicsDevice gd, OutputDescription outputDescription, int windowWidth, int windowHeight)
        {
            _gd = gd;
            _windowWidth = windowWidth;
            _windowHeight = windowHeight;

            ImGui.CreateContext();
            ImGuiIOPtr io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard |
                ImGuiConfigFlags.DockingEnable;
            io.Fonts.Flags |= ImFontAtlasFlags.NoBakedLines;

            font20 = io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 20);
            font30 = io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 30);
            font40 = io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 40);

            // Create device resources

            ResourceFactory factory = gd.ResourceFactory;
            _vertexBuffer = factory.CreateBuffer(new BufferDescription(10000, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            _vertexBuffer.Name = "ImGui.NET Vertex Buffer";
            _indexBuffer = factory.CreateBuffer(new BufferDescription(2000, BufferUsage.IndexBuffer | BufferUsage.Dynamic));
            _indexBuffer.Name = "ImGui.NET Index Buffer";

            // Recreate font device texture

            // Build
            IntPtr pixels;
            int width, height, bytesPerPixel;
            io.Fonts.GetTexDataAsRGBA32(out pixels, out width, out height, out bytesPerPixel);
            // Store our identifier
            io.Fonts.SetTexID(_fontAtlasID);

            _fontTexture = gd.ResourceFactory.CreateTexture(TextureDescription.Texture2D(
                (uint)width,
                (uint)height,
                1,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));
            _fontTexture.Name = "ImGui.NET Font Texture";
            gd.UpdateTexture(
                _fontTexture,
                pixels,
                (uint)(bytesPerPixel * width * height),
                0,
                0,
                0,
                (uint)width,
                (uint)height,
                1,
                0,
                0);
            _fontTextureView = gd.ResourceFactory.CreateTextureView(_fontTexture);

            io.Fonts.ClearTexData();

            // End recreate font device texture

            _projMatrixBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer | BufferUsage.Dynamic));
            _projMatrixBuffer.Name = "ImGui.NET Projection Buffer";

            byte[] vertexShaderBytes = LoadEmbeddedShaderCode(gd.ResourceFactory, "imgui-vertex", ShaderStages.Vertex);
            byte[] fragmentShaderBytes = LoadEmbeddedShaderCode(gd.ResourceFactory, "imgui-frag", ShaderStages.Fragment);
            _vertexShader = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, vertexShaderBytes, gd.BackendType == GraphicsBackend.Metal ? "VS" : "main"));
            _fragmentShader = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, fragmentShaderBytes, gd.BackendType == GraphicsBackend.Metal ? "FS" : "main"));

            VertexLayoutDescription[] vertexLayouts = new VertexLayoutDescription[]
            {
                new VertexLayoutDescription(
                    new VertexElementDescription("in_position", VertexElementSemantic.Position, VertexElementFormat.Float2),
                    new VertexElementDescription("in_texCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                    new VertexElementDescription("in_color", VertexElementSemantic.Color, VertexElementFormat.Byte4_Norm))
            };

            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("ProjectionMatrixBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
            _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment)));

            GraphicsPipelineDescription pd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                new DepthStencilStateDescription(false, false, ComparisonKind.Always),
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, true),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(vertexLayouts, new[] { _vertexShader, _fragmentShader }),
                new ResourceLayout[] { _layout, _textureLayout },
                outputDescription,
                ResourceBindingModel.Default);
            _pipeline = factory.CreateGraphicsPipeline(ref pd);

            _mainResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_layout,
                _projMatrixBuffer,
                gd.PointSampler));

            _fontTextureResourceSet = factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, _fontTextureView));

            //

            SetPerFrameImGuiData(1f / 60f);
            ImGui.NewFrame();
            _frameBegun = true;
        }

        private byte[] LoadEmbeddedShaderCode(ResourceFactory factory, string name, ShaderStages stage)
        {
            switch (factory.BackendType)
            {
                case GraphicsBackend.Direct3D11:
                    {
                        string resourceName = name + ".hlsl.bytes";
                        return GetEmbeddedResourceBytes(resourceName);
                    }
                case GraphicsBackend.OpenGL:
                    {
                        string resourceName = name + ".glsl";
                        return GetEmbeddedResourceBytes(resourceName);
                    }
                case GraphicsBackend.Vulkan:
                    {
                        string resourceName = name + ".spv";
                        return GetEmbeddedResourceBytes(resourceName);
                    }
                case GraphicsBackend.Metal:
                    {
                        string resourceName = name + ".metallib";
                        return GetEmbeddedResourceBytes(resourceName);
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        private byte[] GetEmbeddedResourceBytes(string resourceName)
        {
            Assembly assembly = typeof(ImGuiController).Assembly;
            using (Stream? s = assembly.GetManifestResourceStream(resourceName))
            {
                if (s == null) throw new FileNotFoundException("Resource not found", resourceName);

                byte[] ret = new byte[s.Length];
                s.Read(ret, 0, (int)s.Length);
                return ret;
            }
        }

        /// <summary>
        /// Frees all graphics resources used by the renderer.
        /// </summary>
        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
            _projMatrixBuffer.Dispose();
            _fontTexture.Dispose();
            _fontTextureView.Dispose();
            _vertexShader.Dispose();
            _fragmentShader.Dispose();
            _layout.Dispose();
            _textureLayout.Dispose();
            _pipeline.Dispose();
            _mainResourceSet.Dispose();

            foreach (IDisposable resource in _ownedResources)
            {
                resource.Dispose();
            }
        }

        public void WindowResized(int width, int height)
        {
            _windowWidth = width;
            _windowHeight = height;
        }

        ///// <summary>
        ///// Gets or creates a handle for a texture to be drawn with ImGui.
        ///// Pass the returned handle to Image() or ImageButton().
        ///// </summary>
        //public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, TextureView textureView)
        //{
        //    if (!_setsByView.TryGetValue(textureView, out ResourceSetInfo rsi))
        //    {
        //        ResourceSet resourceSet = factory.CreateResourceSet(new ResourceSetDescription(_textureLayout, textureView));
        //        rsi = new ResourceSetInfo(GetNextImGuiBindingID(), resourceSet);

        //        _setsByView.Add(textureView, rsi);
        //        _viewsById.Add(rsi.ImGuiBinding, rsi);
        //        _ownedResources.Add(resourceSet);
        //    }

        //    return rsi.ImGuiBinding;
        //}

        //private IntPtr GetNextImGuiBindingID()
        //{
        //    int newID = _lastAssignedID++;
        //    return (IntPtr)newID;
        //}

        ///// <summary>
        ///// Gets or creates a handle for a texture to be drawn with ImGui.
        ///// Pass the returned handle to Image() or ImageButton().
        ///// </summary>
        //public IntPtr GetOrCreateImGuiBinding(ResourceFactory factory, Texture texture)
        //{
        //    if (!_autoViewsByTexture.TryGetValue(texture, out TextureView? textureView))
        //    {
        //        textureView = factory.CreateTextureView(texture);
        //        _autoViewsByTexture.Add(texture, textureView);
        //        _ownedResources.Add(textureView);
        //    }

        //    return GetOrCreateImGuiBinding(factory, textureView);
        //}

        //public void ClearCachedImageResources()
        //{
        //    foreach (IDisposable resource in _ownedResources)
        //    {
        //        resource.Dispose();
        //    }

        //    _ownedResources.Clear();
        //    _setsByView.Clear();
        //    _viewsById.Clear();
        //    _autoViewsByTexture.Clear();
        //    _lastAssignedID = 100;
        //}

        /// <summary>
        /// Updates ImGui input and IO configuration state.
        /// </summary>
        public void Update(float deltaSeconds, InputSnapshot snapshot)
        {
            if (_frameBegun)
            {
                ImGui.Render();
            }

            SetPerFrameImGuiData(deltaSeconds);
            UpdateImGuiInput(snapshot);

            _frameBegun = true;
            ImGui.NewFrame();
        }

        /// <summary>
        /// Sets per-frame data based on the associated window.
        /// This is called by Update(float).
        /// </summary>
        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_windowWidth, _windowHeight);
            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
        }

        // Input

        private void UpdateImGuiInput(InputSnapshot snapshot)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.AddMousePosEvent(snapshot.MousePosition.X, snapshot.MousePosition.Y);
            io.AddMouseButtonEvent(0, snapshot.IsMouseDown(MouseButton.Left));
            io.AddMouseButtonEvent(1, snapshot.IsMouseDown(MouseButton.Right));
            io.AddMouseButtonEvent(2, snapshot.IsMouseDown(MouseButton.Middle));
            io.AddMouseButtonEvent(3, snapshot.IsMouseDown(MouseButton.Button1));
            io.AddMouseButtonEvent(4, snapshot.IsMouseDown(MouseButton.Button2));
            io.AddMouseWheelEvent(0f, snapshot.WheelDelta);
            for (int i = 0; i < snapshot.KeyCharPresses.Count; i++)
            {
                io.AddInputCharacter(snapshot.KeyCharPresses[i]);
            }

            for (int i = 0; i < snapshot.KeyEvents.Count; i++)
            {
                KeyEvent keyEvent = snapshot.KeyEvents[i];
                if (TryMapKey(keyEvent.Key, out ImGuiKey imguikey))
                {
                    io.AddKeyEvent(imguikey, keyEvent.Down);
                }
            }
        }

        private bool TryMapKey(Key key, out ImGuiKey result)
        {
            ImGuiKey KeyToImGuiKeyShortcut(Key keyToConvert, Key startKey1, ImGuiKey startKey2)
            {
                int changeFromStart1 = (int)keyToConvert - (int)startKey1;
                return startKey2 + changeFromStart1;
            }

            result = key switch
            {
                >= Key.F1 and <= Key.F12 => KeyToImGuiKeyShortcut(key, Key.F1, ImGuiKey.F1),
                >= Key.Keypad0 and <= Key.Keypad9 => KeyToImGuiKeyShortcut(key, Key.Keypad0, ImGuiKey.Keypad0),
                >= Key.A and <= Key.Z => KeyToImGuiKeyShortcut(key, Key.A, ImGuiKey.A),
                >= Key.Number0 and <= Key.Number9 => KeyToImGuiKeyShortcut(key, Key.Number0, ImGuiKey._0),
                Key.ShiftLeft or Key.ShiftRight => ImGuiKey.ModShift,
                Key.ControlLeft or Key.ControlRight => ImGuiKey.ModCtrl,
                Key.AltLeft or Key.AltRight => ImGuiKey.ModAlt,
                Key.WinLeft or Key.WinRight => ImGuiKey.ModSuper,
                Key.Menu => ImGuiKey.Menu,
                Key.Up => ImGuiKey.UpArrow,
                Key.Down => ImGuiKey.DownArrow,
                Key.Left => ImGuiKey.LeftArrow,
                Key.Right => ImGuiKey.RightArrow,
                Key.Enter => ImGuiKey.Enter,
                Key.Escape => ImGuiKey.Escape,
                Key.Space => ImGuiKey.Space,
                Key.Tab => ImGuiKey.Tab,
                Key.BackSpace => ImGuiKey.Backspace,
                Key.Insert => ImGuiKey.Insert,
                Key.Delete => ImGuiKey.Delete,
                Key.PageUp => ImGuiKey.PageUp,
                Key.PageDown => ImGuiKey.PageDown,
                Key.Home => ImGuiKey.Home,
                Key.End => ImGuiKey.End,
                Key.CapsLock => ImGuiKey.CapsLock,
                Key.ScrollLock => ImGuiKey.ScrollLock,
                Key.PrintScreen => ImGuiKey.PrintScreen,
                Key.Pause => ImGuiKey.Pause,
                Key.NumLock => ImGuiKey.NumLock,
                Key.KeypadDivide => ImGuiKey.KeypadDivide,
                Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
                Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
                Key.KeypadAdd => ImGuiKey.KeypadAdd,
                Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
                Key.KeypadEnter => ImGuiKey.KeypadEnter,
                Key.Tilde => ImGuiKey.GraveAccent,
                Key.Minus => ImGuiKey.Minus,
                Key.Plus => ImGuiKey.Equal,
                Key.BracketLeft => ImGuiKey.LeftBracket,
                Key.BracketRight => ImGuiKey.RightBracket,
                Key.Semicolon => ImGuiKey.Semicolon,
                Key.Quote => ImGuiKey.Apostrophe,
                Key.Comma => ImGuiKey.Comma,
                Key.Period => ImGuiKey.Period,
                Key.Slash => ImGuiKey.Slash,
                Key.BackSlash or Key.NonUSBackSlash => ImGuiKey.Backslash,
                _ => ImGuiKey.None
            };

            return result != ImGuiKey.None;
        }

        // Render

        /// <summary>
        /// Renders the ImGui draw list data.
        /// This method requires a <see cref="GraphicsDevice"/> because it may create new DeviceBuffers if the size of vertex
        /// or index data has increased beyond the capacity of the existing buffers.
        /// A <see cref="CommandList"/> is needed to submit drawing and resource update commands.
        /// </summary>
        public void Render(GraphicsDevice gd, CommandList cl)
        {
            if (_frameBegun)
            {
                _frameBegun = false;
                ImGui.Render();
                RenderImDrawData(ImGui.GetDrawData(), gd, cl);
            }
        }

        private void RenderImDrawData(ImDrawDataPtr draw_data, GraphicsDevice gd, CommandList cl)
        {
            uint vertexOffsetInVertices = 0;
            uint indexOffsetInElements = 0;

            if (draw_data.CmdListsCount == 0)
            {
                return;
            }

            uint totalVBSize = (uint)(draw_data.TotalVtxCount * Unsafe.SizeOf<ImDrawVert>());
            if (totalVBSize > _vertexBuffer.SizeInBytes)
            {
                gd.DisposeWhenIdle(_vertexBuffer);
                _vertexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalVBSize * 1.5f), BufferUsage.VertexBuffer | BufferUsage.Dynamic));
            }

            uint totalIBSize = (uint)(draw_data.TotalIdxCount * sizeof(ushort));
            if (totalIBSize > _indexBuffer.SizeInBytes)
            {
                gd.DisposeWhenIdle(_indexBuffer);
                _indexBuffer = gd.ResourceFactory.CreateBuffer(new BufferDescription((uint)(totalIBSize * 1.5f), BufferUsage.IndexBuffer | BufferUsage.Dynamic));
            }

            for (int i = 0; i < draw_data.CmdListsCount; i++)
            {
                ImDrawListPtr cmd_list = draw_data.CmdListsRange[i];

                cl.UpdateBuffer(
                    _vertexBuffer,
                    vertexOffsetInVertices * (uint)Unsafe.SizeOf<ImDrawVert>(),
                    cmd_list.VtxBuffer.Data,
                    (uint)(cmd_list.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>()));

                cl.UpdateBuffer(
                    _indexBuffer,
                    indexOffsetInElements * sizeof(ushort),
                    cmd_list.IdxBuffer.Data,
                    (uint)(cmd_list.IdxBuffer.Size * sizeof(ushort)));

                vertexOffsetInVertices += (uint)cmd_list.VtxBuffer.Size;
                indexOffsetInElements += (uint)cmd_list.IdxBuffer.Size;
            }

            // Setup orthographic projection matrix into our constant buffer
            ImGuiIOPtr io = ImGui.GetIO();
            Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(
                0f,
                io.DisplaySize.X,
                io.DisplaySize.Y,
                0.0f,
                -1.0f,
                1.0f);

            _gd.UpdateBuffer(_projMatrixBuffer, 0, ref mvp);

            cl.SetVertexBuffer(0, _vertexBuffer);
            cl.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _mainResourceSet);

            draw_data.ScaleClipRects(io.DisplayFramebufferScale);

            // Render command lists
            int vtx_offset = 0;
            int idx_offset = 0;
            for (int n = 0; n < draw_data.CmdListsCount; n++)
            {
                ImDrawListPtr cmd_list = draw_data.CmdListsRange[n];
                for (int cmd_i = 0; cmd_i < cmd_list.CmdBuffer.Size; cmd_i++)
                {
                    ImDrawCmdPtr pcmd = cmd_list.CmdBuffer[cmd_i];
                    if (pcmd.UserCallback != IntPtr.Zero)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        if (pcmd.TextureId != IntPtr.Zero)
                        {
                            if (pcmd.TextureId == _fontAtlasID)
                            {
                                cl.SetGraphicsResourceSet(1, _fontTextureResourceSet);
                            }
                            else
                            {
                                cl.SetGraphicsResourceSet(1, GetImageResourceSet(pcmd.TextureId));
                            }
                        }

                        cl.SetScissorRect(
                            0,
                            (uint)pcmd.ClipRect.X,
                            (uint)pcmd.ClipRect.Y,
                            (uint)(pcmd.ClipRect.Z - pcmd.ClipRect.X),
                            (uint)(pcmd.ClipRect.W - pcmd.ClipRect.Y));

                        cl.DrawIndexed(pcmd.ElemCount, 1, pcmd.IdxOffset + (uint)idx_offset, (int)pcmd.VtxOffset + vtx_offset, 0);
                    }
                }
                vtx_offset += cmd_list.VtxBuffer.Size;
                idx_offset += cmd_list.IdxBuffer.Size;
            }
        }

        /// <summary>
        /// Retrieves the shader texture binding for the given helper handle.
        /// </summary>
        public ResourceSet GetImageResourceSet(IntPtr imGuiBinding)
        {
            if (!_viewsById.TryGetValue(imGuiBinding, out ResourceSetInfo tvi))
            {
                throw new InvalidOperationException("No registered ImGui binding with id " + imGuiBinding.ToString());
            }

            return tvi.ResourceSet;
        }
    }

    public class RomInfo
    {
        public string name;
        public HashSet<string> regions;
        public HashSet<string> languages;
        public HashSet<string> additionalProperties;

        public RomInfo(string name, IEnumerable<string> regions, IEnumerable<string> languages, IEnumerable<string> additionalProperties)
        {
            this.name = name;
            this.regions = new HashSet<string>(regions);
            this.languages = new HashSet<string>(languages);
            this.additionalProperties = new HashSet<string>(additionalProperties);
        }

        public override string ToString()
        {
            return $"{name} [{String.Join(",", regions)}] [{String.Join(",", languages)}] [{String.Join(",", additionalProperties)}]";
        }

        public string PropsString()
        {
            return String.Join(" | ", new[] { regions, languages, additionalProperties }
                .Where(set => set.Count > 0)
                .Select(set => String.Join(", ", set)));
        }
    }

    static class ROMNameParser
    {
        // Apologies if some of this is wrong, I do not speak all of these languages lol

        static string[] allArticles = new[]
        {
            "The", "A", "An", // English
            "El", "La", "Los", "Las", // Spanish
            "Il", "L'", "La", "I", "Gli", "Le", // Italian (there seem to more than this but it's all I see in the names)
            "Die", "Der", "Das", "Ein", // German (there seem to be way more than this but it's all I see in the names)
            "Le", "Les", "L'", "La", "Un", "Une", "Des", // French
            "As", // Portugese (only one article b/c only one game w/ this article problem at the moment)
            "Het", "De", "Een", // Dutch
        }.Distinct().ToArray();

        public static readonly HashSet<string> VALID_REGIONS = new HashSet<string>
        { 
            // Countries
            "Argentina",
            "Australia",
            "Austria",
            "Belgium",
            "Brazil",
            "Canada",
            "China",
            "Croatia",
            "Czech",
            "Denmark",
            "Finland",
            "France",
            "Germany",
            "Greece",
            "Hong Kong",
            "India",
            "Ireland",
            "Israel",
            "Italy",
            "Japan",
            "Korea",
            "Mexico",
            "Netherlands",
            "New Zealand",
            "Norway",
            "Poland",
            "Portugal",
            "Russia",
            "South Africa",
            "Spain",
            "Sweden",
            "Switzerland",
            "Taiwan",
            "Turkey",
            "UK",
            "USA",
            "United Kingdom",

            // Multi-country areas
            "Scandinavia",
            "Europe",
            "Asia",
            "Latin America",

            // Other
            "World",
            "Unknown"
        };

        public static bool IsBios(string path)
        {
            return Path.GetFileName(path).StartsWith("[BIOS]");
        }

        public static RomInfo Parse(string name)
        {
            Debug.Assert(!IsBios(name));

            string matchGameName = @"(?<gamename>.+?)";
            string matchProps = @"( \((?<props>[^\(\)]+?)\))*";
            // This is only ever [b], we don't care about bad-dump-ness though
            string matchStatus = @"( \[(?<status>.+?)\])?";

            Regex rx = new Regex($"^{matchGameName}{matchProps}{matchStatus}$", RegexOptions.ExplicitCapture);
            Match match = rx.Match(name);

            //If there are things in parenthesis at the end of the name, re-add them (since they got misclassified as properties)
            List<string> maybeProps = GetCapturesForGroup(match, "props");

            List<string>? props = null;
            List<string>? falseProps = null;
            for (int i = 0; i < maybeProps.Count; i++)
            {
                string[] potentialRegions = maybeProps[i].Split(", ");
                if (VALID_REGIONS.IsSupersetOf(potentialRegions))
                {
                    props = maybeProps.Skip(i).ToList();
                    falseProps = maybeProps.Take(i).ToList();
                    break;
                }
            }

            if (props == null || falseProps == null)
            {
                List<string> empty = new List<string>();
                return new RomInfo(FixArticlesInName(name), empty, empty, empty);
                //throw new Exception("No region for game");
            }

            string gameName = GetCaptureForGroup(match, "gamename") + String.Join("", falseProps.Select(p => $" ({p})"));
            gameName = FixArticlesInName(gameName);
            string[] regions = props[0].Split(", ");
            string[] languages = { };

            // TODO: En,Ja,Fr,De,Es,It,Zh-Hant,Zh-Hans,Ko (3ds pokemon moon)

            // Next up is language, which is optional
            // N-games-in-one have N sets of languages, separated by a +
            Regex langRx = new Regex(@"^[A-Z][a-z]([,+][A-Z][a-z])*$");
            IEnumerable<string> remainingProps;
            if (props.Count > 1 && langRx.IsMatch(props[1]))
            {
                languages = props[1].Split(',', '+');
                remainingProps = props.Skip(2);
            }
            else
            {
                remainingProps = props.Skip(1);
            }

            return new RomInfo(gameName, regions, languages, remainingProps);
        }

        static List<string> GetCapturesForGroup(Match match, String groupName)
        {
            return match.Groups.Cast<Group>()
                        .Single(group => group.Name == groupName)
                        .Captures
                        .Cast<Capture>()
                        .Select(capture => capture.Value)
                        .ToList();
        }

        static string? GetCaptureForGroup(Match match, String groupName)
        {
            return GetCapturesForGroup(match, groupName).SingleOrDefault();
        }

        static string FixArticlesInName(string name)
        {
            // NOTE: this doesn't handle
            // Sugoroku, The '92 - Nari Tore - Nariagari Trendy    [should be "The Sugoroku '92"]
            // or War in the Gulf ~ Guerra del Golfo, La

            if (name.Contains(" + "))
            {
                return String.Join(" + ", name.Split(" + ").Select(n => FixArticlesInName(n)));
            }

            string fixedName = name;
            // Now we know we're only working with a single title.
            foreach (string article in allArticles)
            {
                string articleInsert;
                if (article.EndsWith("'")) // L'
                    articleInsert = article;
                else
                    articleInsert = article + " ";

                if (name.EndsWith($", {article}"))
                {
                    fixedName = articleInsert + name.Remove(name.Length - (2 + article.Length));
                    break;
                }

                if (name.Contains($", {article} - "))
                {
                    fixedName = articleInsert + name.Replace($", {article} - ", " - ");
                    break;
                }

                if (name.Contains($", {article} ~ "))
                {
                    fixedName = articleInsert + name.Replace($", {article} ~ ", " ~ ");
                    break;
                }
            }

            return fixedName;
        }
    }
}