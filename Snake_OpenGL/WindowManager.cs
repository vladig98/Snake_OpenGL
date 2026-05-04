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
    private static readonly List<Snake> _snake = [];
    private static Snake _food = new();

    private static int _maxX = 0;
    private static int _maxY = 0;

    private static int _dirX = 1;
    private static int _dirY = 0;

    private static readonly Stopwatch _gameLoop = new();
    private static readonly int _updateTime = 200;

    private static bool _isGameOver = false;

    private const string VertexShaderSource = @"#version 330 core
        layout (location = 0) in vec3 aPosition;
        void main() { gl_Position = vec4(aPosition, 1.0); }";

    private const string FragmentShaderSource = @"#version 330 core
        out vec4 out_color;
        uniform vec4 u_Color;
        void main() { out_color = u_Color; }";

    public static void Initialize()
    {
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(BaseWidth, BaseHeight),
            Title = "Snake Game"
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

        for (int i = 5; i >= 0; i--)
        {
            _snake.Add(new Snake(X: i, Y: 0));
        }

        _food = PickLocation();

        _gameLoop.Start();
        _window.Run();
    }

    private static Snake PickLocation()
    {
        List<ulong> positons = [];
        for (int i = 0; i < _maxX; i++)
        {
            for (int j = 0; j < _maxY; j++)
            {
                ulong pos = PackBits(i, j);
                positons.Add(pos);
            }
        }

        for (int i = 0; i < _snake.Count; i++)
        {
            Snake current = _snake[i];
            ulong snakePos = PackBits(current.X, current.Y);
            positons.Remove(snakePos);
        }

        int index = Random.Shared.Next(0, positons.Count);
        ulong foodPos = positons[index];

        (int x, int y) = UnpackBits(foodPos);

        return new Snake(X: x, Y: y);
    }

    private static (int x, int y) UnpackBits(ulong pos)
    {
        return ((int)(pos >> 32), (int)pos);
    }

    private static ulong PackBits(int i, int j)
    {
        return ((ulong)(uint)i << 32) | (uint)j;
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

        int colorLocation = _gl.GetUniformLocation(_program, "u_Color");

        // --- 1. DRAW THE HEAD ---
        _gl.Uniform4(colorLocation, 0.0f, 0.0f, 0.0f, 1.0f); // Black
        _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0);

        // --- 2. DRAW THE BODY ---
        // We check for > 12 because 6 indices belong to the head, and 6 belong to the food.
        if (drawingInfo.Indices.Length > 12)
        {
            _gl.Uniform4(colorLocation, 0.62f, 0.588f, 0.573f, 1.0f); // Brown

            // Subtract the head (6) and the food (6) to get just the body
            uint bodyIndicesCount = (uint)drawingInfo.Indices.Length - 12;

            // Skip the first 6 indices (the head)
            _gl.DrawElements(PrimitiveType.Triangles, bodyIndicesCount, DrawElementsType.UnsignedInt, (void*)(6 * sizeof(uint)));
        }

        // --- 3. DRAW THE FOOD ---
        _gl.Uniform4(colorLocation, 1.0f, 0.0f, 0.0f, 1.0f); // Red

        // The food is exactly the last 6 indices in the array
        uint foodOffset = (uint)drawingInfo.Indices.Length - 6;

        // Skip all the indices that belong to the head and the body
        _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)(foodOffset * sizeof(uint)));
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

        // 4 vertices for a square
        const uint _squareFaces = 4;

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

            uint offset = (uint)i * _squareFaces;

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

        float foodWidthOffset = _food.X * scaledWidth;
        float foodHeightOffset = _food.Y * scaledHeight;
        uint foodOffset = (uint)_snake.Count * _squareFaces;

        // top right
        vertices.Add(-1f + scaledWidth + foodWidthOffset); // x
        vertices.Add(1f - foodHeightOffset); // y
        vertices.Add(0f); // z

        // bottom right
        vertices.Add(-1f + scaledWidth + foodWidthOffset); // x
        vertices.Add(1f - scaledHeight - foodHeightOffset); // y
        vertices.Add(0f); // z

        // bottom left
        vertices.Add(-1f + foodWidthOffset); // x
        vertices.Add(1f - scaledHeight - foodHeightOffset); // y
        vertices.Add(0f); // z

        // top left
        vertices.Add(-1f + foodWidthOffset); // x
        vertices.Add(1f - foodHeightOffset); // y
        vertices.Add(0f); // z

        // connecting
        // top right to
        // bottom right to
        // top left
        // triangle 1
        indices.Add(0u + foodOffset);
        indices.Add(1u + foodOffset);
        indices.Add(3u + foodOffset);

        // connecting
        // bottom right to
        // bottom left to
        // top left
        // triangle 2
        indices.Add(1u + foodOffset);
        indices.Add(2u + foodOffset);
        indices.Add(3u + foodOffset);

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

    private static void UpdateSnake()
    {
        if (_isGameOver)
        {
            return;
        }

        int x = _snake[0].X;
        int y = _snake[0].Y;

        x += _dirX;
        y += _dirY;

        if (x < 0)
        {
            x += _maxX;
        }

        if (x >= _maxX)
        {
            x -= _maxX;
        }

        if (y >= _maxY)
        {
            y -= _maxY;
        }

        if (y < 0)
        {
            y += _maxY;
        }

        _isGameOver = IsGameOver(x, y);
        if (_isGameOver)
        {
            return;
        }

        // We eat the food
        if (x == _food.X && y == _food.Y)
        {
            _snake.Insert(0, _food);
            _food = PickLocation();

            return;
        }

        for (int i = _snake.Count - 1; i >= 1; i--)
        {
            Snake predecessorPart = _snake[i - 1];
            _snake[i] = _snake[i] with { X = predecessorPart.X, Y = predecessorPart.Y };
        }

        _snake[0] = _snake[0] with { X = x, Y = y };
    }

    private static bool IsGameOver(int x, int y)
    {
        for (int i = 1; i < _snake.Count; i++)
        {
            if (x == _snake[i].X && y == _snake[i].Y)
            {
                return true;
            }
        }

        return false;
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
                    _dirX = -1;
                    _dirY = 0;
                }

                if (key == Key.Right)
                {
                    _dirX = 1;
                    _dirY = 0;
                }

                if (key == Key.Down)
                {
                    _dirX = 0;
                    _dirY = 1;
                }

                if (key == Key.Up)
                {
                    _dirX = 0;
                    _dirY = -1;
                }
            };
        }
    }

    private static void OnUpdate(double deltaTime) 
    {
        if (_gameLoop.ElapsedMilliseconds > _updateTime)
        {
            UpdateSnake();
            _gameLoop.Restart();
        }
    }
}