using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            public string name;
            public FolderNode folder;
            public Game gameDetails;

            public string Ext => Path.GetExtension(this.path);

            public ROM(string path, FolderNode folder)
            {
                this.path = path;
                this.folder = folder;
                this.name = Path.GetFileNameWithoutExtension(path);
                gameDetails = ROMNameParser.Parse(name);
            }

            private string? GetPlayCommand()
            {
                return (folder.name, Ext) switch
                {
                    ("Gamecube", _) => $"\"C:\\Portable Programs\\Dolphin\\Dolphin.exe\" -e \"{path}\"",
                    (_, ".rvz" or ".wbfs" or ".wad") => $"\"C:\\Portable Programs\\Dolphin\\Dolphin.exe\" -e \"{path}\"",
                    ("Playstation 2", ".bin" or ".iso") => $"\"C:\\Program Files (x86)\\PCSX2\\pcsx2.exe\" \"{path}\"",
                    ("Dreamcast", ".cue") => $"\"C:\\Portable Programs\\Flycast\\flycast.exe\" \"{path}\"",
                    ("Xbox", ".iso") => $"\"C:\\Portable Programs\\Xemu\\xemu.exe\" \"{path}\"",
                    _ => null,
                };
            }

            public bool CanPlay => GetPlayCommand() != null;

            public void Play()
            {
                Debug.Assert(CanPlay);
                Process.Start(GetPlayCommand()!);
            }
        }

        class FolderNode
        {
            public readonly string name;
            public List<FolderNode> subfolders = new List<FolderNode>();
            public List<ROM> roms = new List<ROM>();

            public bool enabled = true;

            public bool IsRomDir => subfolders.Count == 0;
            public int RomCount => IsRomDir ? roms.Count : subfolders.Sum(folder => folder.RomCount);

            public FolderNode(string dirPath)
            {
                this.name = dirPath.Split(Path.DirectorySeparatorChar).Last();

                Populate(dirPath);
            }

            public void Populate(string dirPath)
            {
                string[] dirs = Directory.GetDirectories(dirPath);

                if (dirs.Length == 0)
                {
                    roms = Directory.EnumerateFiles(dirPath)
                        .Where(filePath => !ROMNameParser.IsBios(filePath))
                        .Select(filePath => new ROM(filePath, this))
                        .Where(rom => rom.CanPlay)
                        .ToList();

                    if (roms.Count(rom => rom.Ext == ".cue") == 1 && roms.Count(rom => rom.Ext == ".bin") == roms.Count - 1)
                    {
                        roms = new List<ROM> { roms.Single(rom => rom.Ext == ".cue") };
                    }
                }
                else
                {
                    subfolders = dirs.Except(new[] { @"D:\ROMs\[Extras]", @"D:\ROMs\MAME" }).Select(dir => new FolderNode(dir)).Where(node => node.subfolders.Count > 0 || node.roms.Count > 0).ToList();

                    if (subfolders.All(node => node.IsRomDir && node.roms.Count == 1))
                    {
                        roms = subfolders.Select(node => node.roms[0]).ToList();
                        roms.ForEach(rom => rom.folder = this);
                        subfolders.Clear();
                    }
                }
            }
        }

        static bool _spinning = false;
        static List<ROM> _chosenRoms = new List<ROM>();

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

                ImGui.BeginDisabled(_spinning);

                if (ImGui.Button("Spin"))
                {
                    _spinning = true;
                }

                ImGui.EndDisabled();

                ImGui.SameLine();

                ImGui.BeginDisabled(!_spinning);

                if (ImGui.Button("Stop"))
                {
                    _spinning = false;
                }

                ImGui.EndDisabled();

                if (_spinning)
                {
                    List<ROM> allRoms = new List<ROM>();

                    Stack<Queue<FolderNode>> foldersStack = new Stack<Queue<FolderNode>>();
                    foldersStack.Push(new Queue<FolderNode>(rootNode.subfolders));

                    while (foldersStack.Count > 0)
                    {
                        if (foldersStack.Peek().TryDequeue(out FolderNode nextNode))
                        {
                            if (nextNode.enabled)
                            {
                                if (nextNode.IsRomDir)
                                {
                                    allRoms.AddRange(nextNode.roms);
                                }
                                else
                                {
                                    foldersStack.Push(new Queue<FolderNode>(nextNode.subfolders));
                                }
                            }
                        }
                        else
                        {
                            while (foldersStack.Count > 0 && foldersStack.Peek().Count == 0)
                            {
                                foldersStack.Pop();
                            }
                        }
                    }

                    if (allRoms.Count() == 0)
                    {
                        _spinning = false;
                    }
                    else
                    {
                        _chosenRoms.Clear();
                        int nRoms = 3;

                        for (int i = 0; i < nRoms; i++)
                        {
                            ROM rom = allRoms[Random.Shared.Next(allRoms.Count() - 1)];
                            _chosenRoms.Add(rom);
                        }
                    }
                }

                if (ImGui.BeginTable("roms", 4))
                {
                    ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthFixed, 400);
                    ImGui.TableSetupColumn("##console", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableSetupColumn("##button", ImGuiTableColumnFlags.WidthFixed, 40);
                    ImGui.TableSetupColumn("##fullname", ImGuiTableColumnFlags.WidthStretch);

                    foreach (ROM rom in _chosenRoms)
                    {
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();
                        ImGui.Text(rom.gameDetails.name);

                        ImGui.TableNextColumn();
                        ImGui.Text(rom.folder.name);

                        ImGui.TableNextColumn();
                        if (ImGui.Button($"Play##{rom.path}"))
                        {
                            rom.Play();
                        }

                        ImGui.TableNextColumn();
                        ImGui.Text(rom.name);
                    }

                    ImGui.EndTable();
                }

                // Render folder browser

                {
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
                                if (nextNode.IsRomDir)
                                {
                                    ImGui.PushStyleColor(ImGuiCol.Text, 0xFF00FF00);
                                }

                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();

                                if (ImGui.TreeNode(nextNode.name))
                                {
                                    if (nextNode.IsRomDir)
                                    {
                                        foreach (ROM rom in nextNode.roms)
                                        {
                                            ImGui.Selectable($"{rom.gameDetails} ({rom.name})");
                                        }

                                        ImGui.TreePop();
                                    }
                                    else
                                    {
                                        foldersStack.Push(new Queue<FolderNode>(nextNode.subfolders));
                                    }
                                }

                                if (nextNode.IsRomDir)
                                {
                                    ImGui.PopStyleColor();
                                }

                                ImGui.TableNextColumn();
                                ImGui.Checkbox($"##{nextNode.name}", ref nextNode.enabled);

                                ImGui.TableNextColumn();
                                ImGui.Text($"{nextNode.RomCount}");
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

            io.Fonts.AddFontFromFileTTF(@"C:\Windows\Fonts\segoeui.ttf", 20);

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

    public class Game
    {
        public string name;
        public HashSet<string> regions;
        public HashSet<string> languages;
        public HashSet<string> additionalProperties;

        public Game(string name, IEnumerable<string> regions, IEnumerable<string> languages, IEnumerable<string> additionalProperties)
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
    }

    static class ROMNameParser
    {
        // Apologies if some of this is wrong, I do not speak all of these languages lol
        // TODO: reevaluate these after more stuff is added
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

        public static Game Parse(string name)
        {
            Debug.Assert(!IsBios(name));

            /*
            string matchBIOS = @"(?<biosflag>\[BIOS\] )?";
            string matchGameName = @"(?<gamename>.+?)";
            string matchProps = @"( \((?<props>[^\(\)]+?)\))*";
            // This is only ever [b], we don't care about bad-dump-ness though
            string matchStatus = @"( \[(?<status>.+?)\])?";

            Regex rx = new Regex($"^{matchBIOS}{matchGameName}{matchProps}{matchStatus}$", RegexOptions.ExplicitCapture);
            Match match = rx.Match(name);

            // Ignore bioses
            string? bios = GetCaptureForGroup(match, "biosflag");
            if (bios != null)
            {
                return null;
            }
            */

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
                string[] potentialRegions = maybeProps[i].Split(new[] { ", " }, StringSplitOptions.None);
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
                return new Game(FixArticlesInName(name), empty, empty, empty);
                //throw new Exception("No region for game");
            }

            string gameName = GetCaptureForGroup(match, "gamename") + String.Join("", falseProps.Select(p => $" ({p})"));
            gameName = FixArticlesInName(gameName);
            string[] regions = props[0].Split(new[] { ", " }, StringSplitOptions.None);
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

            return new Game(gameName, regions, languages, remainingProps);
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

            //if (Config.SHOW_RENAMED_GAMES && fixedName != name)
            //{
            //    Console.WriteLine($"RENAMED: \"{name}\" ||| \"{fixedName}\"");
            //}

            return fixedName;
        }
    }
}