// Swarmfall: the playable game. Rendering and input live here; every rule of
// the game lives in Swarmfall.Core, which knows nothing about raylib.
using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using Swarmfall.Core;

const double StepSeconds = 1.0 / 120;

var config = new WorldConfig();
var world = new World(config, (uint)Environment.TickCount);
var clock = new FixedStepClock(StepSeconds, maxStepsPerFrame: 4);

Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
Raylib.InitWindow(1280, 720, "Swarmfall");
Raylib.SetWindowMinSize(640, 360);
Raylib.SetExitKey(KeyboardKey.Escape);

// Capture mode, for README screenshots: --capture out.png <seconds> [extraEnemies] [brute] [grid]
// The player flies itself in a circle and the window closes after the shot.
string? captureFile = args.Length >= 2 && args[0] == "--capture" ? args[1] : null;
double captureAt = captureFile != null && args.Length >= 3 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0;
if (captureFile != null && args.Length >= 4) world.SpawnAtEdges(int.Parse(args[3]));
if (captureFile != null && args.Contains("brute")) world.Broadphase = Broadphase.BruteForce;

bool paused = false;
bool showGrid = false;
bool capFps = false;
double simMsPerStep = 0;   // smoothed
float shake = 0;           // seconds of screen shake left
int lastHealth = world.PlayerHealth;
var hud = new HudText();

while (!Raylib.WindowShouldClose())
{
    // ---- input ----
    if (Raylib.IsKeyPressed(KeyboardKey.G))
        world.Broadphase = world.Broadphase == Broadphase.Grid ? Broadphase.BruteForce : Broadphase.Grid;
    if (Raylib.IsKeyPressed(KeyboardKey.B)) world.SpawnAtEdges(1000);
    if (Raylib.IsKeyPressed(KeyboardKey.F1)) showGrid = !showGrid;
    if (Raylib.IsKeyPressed(KeyboardKey.P)) paused = !paused;
    if (Raylib.IsKeyPressed(KeyboardKey.V)) { capFps = !capFps; Raylib.SetTargetFPS(capFps ? 60 : 0); }
    if (Raylib.IsKeyPressed(KeyboardKey.R)) { world.Reset(); lastHealth = world.PlayerHealth; paused = false; }

    float mx = (Down(KeyboardKey.D) || Down(KeyboardKey.Right) ? 1 : 0) - (Down(KeyboardKey.A) || Down(KeyboardKey.Left) ? 1 : 0);
    float my = (Down(KeyboardKey.S) || Down(KeyboardKey.Down) ? 1 : 0) - (Down(KeyboardKey.W) || Down(KeyboardKey.Up) ? 1 : 0);
    world.MoveInput = captureFile != null ? new Vec2(1, 0).Rotate((float)world.Time * 1.2f) : new Vec2(mx, my);
    if (captureFile != null && args.Contains("grid")) showGrid = true;

    // ---- simulate: a whole number of fixed steps ----
    int steps = paused ? 0 : clock.Advance(Raylib.GetFrameTime());
    if (steps > 0)
    {
        long t0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < steps; i++) world.Step((float)StepSeconds);
        double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds / steps;
        simMsPerStep = simMsPerStep == 0 ? ms : simMsPerStep * 0.9 + ms * 0.1;
    }
    if (world.PlayerHealth < lastHealth) shake = 0.25f;
    lastHealth = world.PlayerHealth;
    shake = MathF.Max(0, shake - Raylib.GetFrameTime());

    // ---- draw: interpolate between the last two steps ----
    float alpha = paused ? 1f : clock.Alpha;
    int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
    float zoom = MathF.Min(sw / config.Width, sh / config.Height);
    var camera = new Camera2D
    {
        Zoom = zoom,
        Offset = new Vector2((sw - config.Width * zoom) / 2, (sh - config.Height * zoom) / 2) + ShakeOffset(shake),
        Target = Vector2.Zero,
    };

    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(8, 10, 18, 255));
    Raylib.BeginMode2D(camera);

    Raylib.DrawRectangleV(Vector2.Zero, new Vector2(config.Width, config.Height), new Color(16, 20, 34, 255));
    if (showGrid) DrawGrid(config);

    for (int i = 0; i < world.Enemies.Count; i++)
    {
        int s = world.Enemies.ActiveAt(i);
        Vector2 p = ToV(Vec2.Lerp(world.EnemyPrevious[s], world.EnemyPosition[s], alpha));
        Color c = world.Enemies[s].HitFlash > 0 ? Color.White : new Color(240, 84, 84, 255);
        Raylib.DrawPoly(p, 6, config.EnemyRadius, world.Enemies[s].Velocity.Angle * (180f / MathF.PI), c);
    }

    for (int i = 0; i < world.Bullets.Count; i++)
    {
        int s = world.Bullets.ActiveAt(i);
        Vec2 p = Vec2.Lerp(world.BulletPrevious[s], world.BulletPosition[s], alpha);
        Vec2 tail = p - world.Bullets[s].Velocity.Normalized() * 10;
        Raylib.DrawLineEx(ToV(tail), ToV(p), config.BulletRadius * 1.4f, new Color(255, 214, 92, 255));
    }

    Vec2 pp = Vec2.Lerp(world.PlayerPrevious, world.PlayerPosition, alpha);
    bool blink = world.PlayerInvulnerable > 0 && (int)(world.Time * 20) % 2 == 0;
    if (!blink)
    {
        Vec2 a = world.AimDirection;
        Vec2 tip = pp + a * (config.PlayerRadius * 1.6f);
        Vec2 left = pp + a.Rotate(2.5f) * config.PlayerRadius;
        Vec2 right = pp + a.Rotate(-2.5f) * config.PlayerRadius;
        Raylib.DrawCircleV(ToV(pp), config.PlayerRadius * 2.2f, new Color(90, 220, 255, 40));
        Raylib.DrawTriangle(ToV(tip), ToV(left), ToV(right), new Color(90, 220, 255, 255));
        Raylib.DrawCircleLinesV(ToV(pp), config.PlayerRadius + 3, new Color(90, 220, 255, 120));
    }
    Raylib.DrawRectangleLinesEx(new Rectangle(0, 0, config.Width, config.Height), 2, new Color(60, 70, 100, 255));
    Raylib.EndMode2D();

    DrawHud(world, sw, sh);
    if (paused) Centered("PAUSED  -  P to resume", sh / 2, 36, Color.White, sw);
    if (world.GameOver)
    {
        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 160));
        Centered("GAME OVER", sh / 2 - 50, 56, new Color(240, 84, 84, 255), sw);
        Centered(hud.GameOver(world), sh / 2 + 20, 26, Color.White, sw);
    }
    Raylib.EndDrawing();

    if (captureFile != null && Raylib.GetTime() >= captureAt)
    {
        Raylib.TakeScreenshot(captureFile);
        break;
    }
}

Raylib.CloseWindow();

// ---------------------------------------------------------------------------

bool Down(KeyboardKey k) => Raylib.IsKeyDown(k);

static Vector2 ToV(Vec2 v) => new(v.X, v.Y);

static Vector2 ShakeOffset(float shake)
{
    if (shake <= 0) return Vector2.Zero;
    float t = (float)Raylib.GetTime() * 90f;
    return new Vector2(MathF.Sin(t), MathF.Cos(t * 1.3f)) * (shake * 24f);
}

static void DrawGrid(WorldConfig config)
{
    // Draw the grid at the size the simulation uses, so the overlay is the real thing.
    float cell = MathF.Max(config.SeparationRadius,
                 MathF.Max(config.PlayerRadius + config.EnemyRadius, config.BulletRadius + config.EnemyRadius));
    var line = new Color(40, 48, 72, 255);
    for (float x = 0; x <= config.Width; x += cell) Raylib.DrawLineV(new Vector2(x, 0), new Vector2(x, config.Height), line);
    for (float y = 0; y <= config.Height; y += cell) Raylib.DrawLineV(new Vector2(0, y), new Vector2(config.Width, y), line);
}

void DrawHud(World w, int sw, int sh)
{
    hud.Refresh(w, simMsPerStep, capFps);

    Raylib.DrawRectangle(0, 0, sw, 64, new Color(0, 0, 0, 150));
    Raylib.DrawText(hud.Top, 16, 10, 24, Color.White);
    Raylib.DrawText(hud.Perf, 16, 38, 18, w.Broadphase == Broadphase.Grid ? new Color(120, 230, 150, 255) : new Color(255, 170, 90, 255));

    // health bar
    int barW = 220;
    float frac = (float)w.PlayerHealth / w.Config.PlayerMaxHealth;
    Raylib.DrawRectangle(sw - barW - 16, 14, barW, 18, new Color(60, 20, 20, 255));
    Raylib.DrawRectangle(sw - barW - 16, 14, (int)(barW * frac), 18, new Color(90, 220, 120, 255));
    Raylib.DrawRectangleLines(sw - barW - 16, 14, barW, 18, Color.White);

    Raylib.DrawText(HudText.Controls, 16, sh - 26, 16, new Color(170, 180, 205, 255));
}

static void Centered(string text, int y, int size, Color color, int sw) =>
    Raylib.DrawText(text, (sw - Raylib.MeasureText(text, size)) / 2, y, size, color);

/// <summary>
/// HUD strings, rebuilt a few times a second rather than every frame, so the
/// render loop is not churning out garbage strings at hundreds of frames a second.
/// </summary>
sealed class HudText
{
    public const string Controls =
        "WASD/arrows move   G grid vs brute force   B +1000 enemies   F1 show grid   V cap 60 FPS   P pause   R restart";

    private double _nextRefresh;
    public string Top { get; private set; } = "";
    public string Perf { get; private set; } = "";

    public void Refresh(World w, double simMs, bool capped)
    {
        double now = Raylib.GetTime();
        if (now < _nextRefresh) return;
        _nextRefresh = now + 0.25;
        Top = $"SWARMFALL    Wave {w.Wave}    Score {w.Score:N0}    Kills {w.Kills:N0}    Next wave {Math.Max(0, w.WaveTimeLeft):F0}s";
        string mode = w.Broadphase == Broadphase.Grid ? "SPATIAL GRID" : "BRUTE FORCE O(n^2)";
        Perf = $"{Raylib.GetFPS()} FPS{(capped ? " (capped)" : "")}    {w.Enemies.Count:N0} enemies    {w.Bullets.Count} bullets    sim {simMs:F2} ms/step    collisions: {mode}";
    }

    public string GameOver(World w) => $"Score {w.Score:N0}  -  Wave {w.Wave}  -  {w.Kills:N0} kills      R to play again";
}
