namespace Snake_OpenGL;

public static class WindowManager
{
    private static IWindow? _window;
    private static GL? _gl;
    private static IInputContext? _input;

    // OpenGL Handles
    private static uint _vao;
    private static uint _vbo;
    private static uint _ebo;
    private static uint _program;

    // State
    private const int BaseWidth = 1800;
    private const int BaseHeight = 600;
    private const int _snakeSize = 50;
    //private static List<int> _snake = [10, 10];
    private static List<Snake> _snake = [new(0, 0), new(1, 0)];

    private static int _maxX = 0;
    private static int _maxY = 0;

    private const string VertexShaderSource = @"#version 330 core
        layout (location = 0) in vec3 aPosition;
        void main() { gl_Position = vec4(aPosition, 1.0); }";

    private const string FragmentShaderSource = @"#version 330 core
        out vec4 out_color;
        void main() { out_color = vec4(1.0, 0.5, 0.2, 1.0); }";

    public static void Initialize()
    {
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(BaseWidth, BaseHeight),
            Title = "Snake Game",
            FramesPerSecond = 10
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += (size) => _gl?.Viewport(0, 0, (uint)size.X, (uint)size.Y);

        int xSquares = _window.Size.X / _snakeSize;
        int ySquares = _window.Size.Y / _snakeSize;

        if (xSquares * _snakeSize != _window.Size.X || ySquares * _snakeSize != _window.Size.Y)
        {
            throw new InvalidOperationException("Unsupported screen size");
        }

        _maxX = xSquares;
        _maxY = ySquares;

        _window.Run();
    }

    private static unsafe void OnLoad()
    {
        _gl = _window!.CreateOpenGL();
        SetupInput();

        // 1. Shaders
        _program = CreateProgram(VertexShaderSource, FragmentShaderSource);

        // 2. Buffer Initialization
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);

        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        // 3. Vertex Attributes (Telling OpenGL how to read the VBO)
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);

        _gl.ClearColor(Color.CornflowerBlue);
    }

    private static unsafe void OnRender(double deltaTime)
    {
        if (_gl is null || _window is null)
        {
            return;
        }

        _gl.Clear(ClearBufferMask.ColorBufferBit);

        DrawingInfo drawingInfo = GenerateGeometry();
        UpdateBuffers(drawingInfo);

        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)drawingInfo.Indices.Length, DrawElementsType.UnsignedInt, (void*)0);
    }

    private static DrawingInfo GenerateGeometry()
    {
        List<float> vertices = [];
        List<uint> indices = [];

        float windowWidth = _window!.Size.X;
        float windowHeight = _window!.Size.Y;

        float percentageOfWidth = _snakeSize / windowWidth;
        float percentageOfHeight = _snakeSize / windowHeight;

        // -1 to 1
        const float openGlScale = 2.0f;

        float scaledWidth = percentageOfWidth * openGlScale;
        float scaledHeight = percentageOfHeight * openGlScale;

        for (int i = 0; i < _snake.Count; i++)
        {
            Snake part = _snake[i];

            float widthOffset = part.X * scaledWidth;
            float heightOffset = part.Y * scaledHeight;

            // top right
            vertices.Add(-1f + scaledWidth + widthOffset); // x
            vertices.Add(1f - heightOffset); // y
            vertices.Add(0f); // z

            // bottom right
            vertices.Add(-1f + scaledWidth + widthOffset); // x
            vertices.Add(1f - scaledHeight - heightOffset); // y
            vertices.Add(0f); // z

            // bottom left
            vertices.Add(-1f + widthOffset); // x
            vertices.Add(1f - scaledHeight - heightOffset); // y
            vertices.Add(0f); // z

            // top left
            vertices.Add(-1f + widthOffset); // x
            vertices.Add(1f - heightOffset); // y
            vertices.Add(0f); // z

            // 4 vertices for a square
            uint offset = (uint)i * 4;

            // connecting
            // top right to
            // bottom right to
            // top left
            // triangle 1
            indices.Add(0u + offset);
            indices.Add(1u + offset);
            indices.Add(3u + offset);

            // connecting
            // bottom right to
            // bottom left to
            // top left
            // triangle 2
            indices.Add(1u + offset);
            indices.Add(2u + offset);
            indices.Add(3u + offset);
        }

        return new DrawingInfo([.. vertices], [.. indices]);
    }

    private static unsafe void UpdateBuffers(DrawingInfo drawingInfo)
    {
        _gl!.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* vPtr = drawingInfo.Vertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(drawingInfo.Vertices.Length * sizeof(float)), vPtr, BufferUsageARB.StreamDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* iPtr = drawingInfo.Indices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(drawingInfo.Indices.Length * sizeof(uint)), iPtr, BufferUsageARB.StreamDraw);
        }
    }

    private static uint CreateProgram(string vCode, string fCode)
    {
        uint vShader = CompileShader(ShaderType.VertexShader, vCode);
        uint fShader = CompileShader(ShaderType.FragmentShader, fCode);

        uint prog = _gl!.CreateProgram();
        _gl.AttachShader(prog, vShader);
        _gl.AttachShader(prog, fShader);
        _gl.LinkProgram(prog);

        _gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int status);
        if (status != (int)GLEnum.True)
        {
            throw new Exception($"Program Link Error: {_gl.GetProgramInfoLog(prog)}");
        }

        _gl.DeleteShader(vShader);
        _gl.DeleteShader(fShader);
        return prog;
    }

    private static uint CompileShader(ShaderType type, string code)
    {
        uint shader = _gl!.CreateShader(type);
        _gl.ShaderSource(shader, code);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status != (int)GLEnum.True)
        {
            throw new Exception($"{type} Error: {_gl.GetShaderInfoLog(shader)}");
        }

        return shader;
    }

    private static void SetupInput()
    {
        _input = _window!.CreateInput();
        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (k, key, _) => 
            { 
                if (key == Key.Escape) 
                { 
                    _window!.Close(); 
                }

                if (key == Key.Left)
                {
                    for (int i = _snake.Count - 1; i >= 0; i--)
                    {
                        Snake snakePart = _snake[i];
                        int x = snakePart.X;
                        x--;

                        if (x < 0)
                        {
                            x += _maxX;
                        }

                        _snake[i] = snakePart with { X = x };
                    }
                }

                if (key == Key.Right)
                {
                    for (int i = _snake.Count - 1; i >= 0; i--)
                    {
                        Snake snakePart = _snake[i];
                        int x = snakePart.X;
                        x++;

                        if (x >= _maxX)
                        {
                            x -= _maxX;
                        }

                        _snake[i] = snakePart with { X = x };
                    }
                }

                if (key == Key.Down)
                {
                    for (int i = _snake.Count - 1; i >= 0; i--)
                    {
                        Snake snakePart = _snake[i];
                        int y = snakePart.Y;
                        y++;

                        if (y >= _maxY)
                        {
                            y -= _maxY;
                        }

                        _snake[i] = snakePart with { Y = y };
                    }
                }

                if (key == Key.Up)
                {
                    for (int i = _snake.Count - 1; i >= 0; i--)
                    {
                        Snake snakePart = _snake[i];
                        int y = snakePart.Y;
                        y--;

                        if (y < 0)
                        {
                            y += _maxY;
                        }

                        _snake[i] = snakePart with { Y = y };
                    }
                }
            };
        }
    }

    private static void OnUpdate(double deltaTime) 
    {
    }
}