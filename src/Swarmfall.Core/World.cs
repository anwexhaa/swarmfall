namespace Swarmfall.Core;

/// <summary>How the simulation finds nearby entities.</summary>
public enum Broadphase
{
    /// <summary>Uniform spatial grid: each query looks at a 3x3 block of cells.</summary>
    Grid,
    /// <summary>Checks every live enemy: O(n) per query, O(n^2) per step. Kept for comparison.</summary>
    BruteForce,
}

public struct Enemy
{
    public Vec2 Velocity;
    public float Speed;
    public int Health;
    public float HitFlash; // seconds left of the white flash after being hit
}

public struct Bullet
{
    public Vec2 Velocity;
    public float Life;
}

public sealed class WorldConfig
{
    public float Width { get; init; } = 1600f;
    public float Height { get; init; } = 900f;
    public int MaxEnemies { get; init; } = 12000;
    public int MaxBullets { get; init; } = 1024;

    public float PlayerRadius { get; init; } = 12f;
    public float PlayerSpeed { get; init; } = 280f;
    public int PlayerMaxHealth { get; init; } = 100;
    public float PlayerInvulnerableAfterHit { get; init; } = 0.6f;
    public int ContactDamage { get; init; } = 10;

    public float EnemyRadius { get; init; } = 7f;
    public float EnemyMinSpeed { get; init; } = 90f;
    public float EnemyMaxSpeed { get; init; } = 150f;
    public float EnemySteering { get; init; } = 4f;     // how fast velocity turns toward the player
    public float SeparationRadius { get; init; } = 16f; // enemies push apart inside this
    public float SeparationStrength { get; init; } = 2200f;

    public float FireInterval { get; init; } = 0.07f;
    public float FireRange { get; init; } = 520f;
    public float BulletSpeed { get; init; } = 950f;
    public float BulletRadius { get; init; } = 3f;
    public float BulletLife { get; init; } = 1.0f;

    public float WaveSeconds { get; init; } = 20f;

    /// <summary>Waves start on their own. The benchmark turns this off and spawns directly.</summary>
    public bool AutoWaves { get; init; } = true;

    /// <summary>The player never dies. Used by the benchmark so a run lasts its full length.</summary>
    public bool Invulnerable { get; init; }
}

/// <summary>
/// The whole game simulation, with no rendering and no input devices: the game
/// sets MoveInput and calls Step, the benchmark and tests do the same. Step
/// allocates nothing after construction.
/// </summary>
public sealed class World
{
    private const int NeighbourBufferSize = 64;

    private readonly SpatialGrid _grid;
    private readonly int[] _liveSlots;
    private readonly int[] _neighbours = new int[NeighbourBufferSize];
    private XorShift _rng;

    public World(WorldConfig config, uint seed)
    {
        Config = config;
        _rng = new XorShift(seed);
        Enemies = new Pool<Enemy>(config.MaxEnemies);
        EnemyPosition = new Vec2[config.MaxEnemies];
        EnemyPrevious = new Vec2[config.MaxEnemies];
        Bullets = new Pool<Bullet>(config.MaxBullets);
        BulletPosition = new Vec2[config.MaxBullets];
        BulletPrevious = new Vec2[config.MaxBullets];
        _liveSlots = new int[config.MaxEnemies];

        // A cell as wide as the largest query radius means one 3x3 block covers any query.
        float cell = MathF.Max(config.SeparationRadius,
                     MathF.Max(config.PlayerRadius + config.EnemyRadius, config.BulletRadius + config.EnemyRadius));
        _grid = new SpatialGrid(config.Width, config.Height, cell, config.MaxEnemies);

        Reset();
    }

    public WorldConfig Config { get; }
    public Broadphase Broadphase { get; set; } = Broadphase.Grid;

    public Pool<Enemy> Enemies { get; }
    /// <summary>Indexed by enemy slot. Kept apart from Enemy so the grid reads a tight array.</summary>
    public Vec2[] EnemyPosition { get; }
    public Vec2[] EnemyPrevious { get; }

    public Pool<Bullet> Bullets { get; }
    public Vec2[] BulletPosition { get; }
    public Vec2[] BulletPrevious { get; }

    public Vec2 PlayerPosition { get; private set; }
    public Vec2 PlayerPrevious { get; private set; }
    public Vec2 AimDirection { get; private set; } = new(1, 0);
    public int PlayerHealth { get; private set; }
    public float PlayerInvulnerable { get; private set; }

    /// <summary>Movement intent for the next step, any length; it is normalised.</summary>
    public Vec2 MoveInput { get; set; }

    public int Wave { get; private set; }
    public float WaveTimeLeft { get; private set; }
    public int Score { get; private set; }
    public int Kills { get; private set; }
    public double Time { get; private set; }
    public long Steps { get; private set; }
    public bool GameOver { get; private set; }

    private float _fireCooldown;

    public void Reset()
    {
        Enemies.Clear();
        Bullets.Clear();
        PlayerPosition = PlayerPrevious = new Vec2(Config.Width / 2, Config.Height / 2);
        PlayerHealth = Config.PlayerMaxHealth;
        PlayerInvulnerable = 0;
        Wave = 0;
        WaveTimeLeft = 0;
        Score = 0;
        Kills = 0;
        Time = 0;
        Steps = 0;
        GameOver = false;
        _fireCooldown = 0;
        if (Config.AutoWaves) StartNextWave();
    }

    /// <summary>Advances the simulation by exactly dt seconds.</summary>
    public void Step(float dt)
    {
        if (GameOver) return;
        Steps++;
        Time += dt;

        PlayerPrevious = PlayerPosition;
        for (int i = 0; i < Enemies.Count; i++)
        {
            int s = Enemies.ActiveAt(i);
            EnemyPrevious[s] = EnemyPosition[s];
        }
        for (int i = 0; i < Bullets.Count; i++)
        {
            int s = Bullets.ActiveAt(i);
            BulletPrevious[s] = BulletPosition[s];
        }

        MovePlayer(dt);
        RebuildGrid();
        MoveEnemies(dt);
        RebuildGrid(); // enemies moved; the rest of the step needs fresh cells
        ResolveOverlaps();
        Fire(dt);
        MoveBullets(dt);
        CheckPlayerContact(dt);
        UpdateWaves(dt);
    }

    /// <summary>Spawns count enemies at random points around the arena edge.</summary>
    public int SpawnAtEdges(int count)
    {
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            float t = _rng.NextFloat();
            float inset = Config.EnemyRadius;
            Vec2 p = (_rng.NextUInt() & 3) switch
            {
                0 => new(t * Config.Width, inset),
                1 => new(t * Config.Width, Config.Height - inset),
                2 => new(inset, t * Config.Height),
                _ => new(Config.Width - inset, t * Config.Height),
            };
            if (SpawnEnemy(p) < 0) break;
            spawned++;
        }
        return spawned;
    }

    /// <summary>Spawns count enemies anywhere in the arena, away from the player. For the benchmark.</summary>
    public int SpawnScattered(int count)
    {
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            Vec2 p = new(_rng.Range(0, Config.Width), _rng.Range(0, Config.Height));
            if (Vec2.DistanceSquared(p, PlayerPosition) < 150f * 150f) { i--; continue; }
            if (SpawnEnemy(p) < 0) break;
            spawned++;
        }
        return spawned;
    }

    private int SpawnEnemy(Vec2 p)
    {
        var enemy = new Enemy
        {
            Speed = _rng.Range(Config.EnemyMinSpeed, Config.EnemyMaxSpeed),
            Health = 2 + Wave / 3,
        };
        int slot = Enemies.Spawn(enemy);
        if (slot >= 0) EnemyPosition[slot] = EnemyPrevious[slot] = p;
        return slot;
    }

    private void MovePlayer(float dt)
    {
        Vec2 v = MoveInput.Normalized() * Config.PlayerSpeed;
        PlayerPosition = ClampToArena(PlayerPosition + v * dt, Config.PlayerRadius);
        if (PlayerInvulnerable > 0) PlayerInvulnerable = MathF.Max(0, PlayerInvulnerable - dt);
    }

    private void RebuildGrid()
    {
        if (Broadphase != Broadphase.Grid) return;
        int n = Enemies.Count;
        for (int i = 0; i < n; i++) _liveSlots[i] = Enemies.ActiveAt(i);
        _grid.Build(_liveSlots.AsSpan(0, n), EnemyPosition);
    }

    /// <summary>Live enemies within radius of center, via whichever broadphase is selected.</summary>
    private int FindEnemiesNear(Vec2 center, float radius, Span<int> results)
    {
        if (Broadphase == Broadphase.Grid)
            return _grid.Query(center, radius, EnemyPosition, results);

        float r2 = radius * radius;
        int found = 0;
        for (int i = 0; i < Enemies.Count && found < results.Length; i++)
        {
            int s = Enemies.ActiveAt(i);
            if (Vec2.DistanceSquared(EnemyPosition[s], center) <= r2) results[found++] = s;
        }
        return found;
    }

    private int FindNearestEnemy(Vec2 center, float maxRadius)
    {
        if (Broadphase == Broadphase.Grid)
            return _grid.Nearest(center, maxRadius, EnemyPosition);

        int best = -1;
        float bestD2 = maxRadius * maxRadius;
        for (int i = 0; i < Enemies.Count; i++)
        {
            int s = Enemies.ActiveAt(i);
            float d2 = Vec2.DistanceSquared(EnemyPosition[s], center);
            if (d2 < bestD2) { bestD2 = d2; best = s; }
        }
        return best;
    }

    private void MoveEnemies(float dt)
    {
        Span<int> near = _neighbours;
        float sepR = Config.SeparationRadius;
        for (int i = 0; i < Enemies.Count; i++)
        {
            int s = Enemies.ActiveAt(i);
            ref Enemy e = ref Enemies[s];
            Vec2 p = EnemyPosition[s];

            // Seek: steer velocity toward the player at this enemy's top speed.
            Vec2 desired = (PlayerPosition - p).Normalized() * e.Speed;
            Vec2 steer = (desired - e.Velocity) * (Config.EnemySteering * dt);

            // Separation: push away from close neighbours, harder the closer they are.
            Vec2 push = Vec2.Zero;
            int count = FindEnemiesNear(p, sepR, near);
            for (int k = 0; k < count; k++)
            {
                int o = near[k];
                if (o == s) continue;
                Vec2 away = p - EnemyPosition[o];
                float d2 = away.LengthSquared;
                if (d2 > 1e-4f) push += away / d2;
            }

            e.Velocity = (e.Velocity + steer + push * (Config.SeparationStrength * dt)).ClampLength(e.Speed * 1.5f);
            EnemyPosition[s] = ClampToArena(p + e.Velocity * dt, Config.EnemyRadius);
            if (e.HitFlash > 0) e.HitFlash = MathF.Max(0, e.HitFlash - dt);
        }
    }

    /// <summary>
    /// Separation steering alone lets a big swarm squash into one blob, because
    /// hundreds of enemies all seek the same point. This pass pushes apart any two
    /// enemies that overlap, half the overlap each, so a swarm packs like a crowd.
    /// The moves are smaller than an enemy, so the grid built just before stays
    /// good enough for the rest of the step.
    /// </summary>
    private void ResolveOverlaps()
    {
        Span<int> near = _neighbours;
        float minDist = Config.EnemyRadius * 2;
        for (int i = 0; i < Enemies.Count; i++)
        {
            int s = Enemies.ActiveAt(i);
            Vec2 p = EnemyPosition[s];
            int count = FindEnemiesNear(p, minDist, near);
            for (int k = 0; k < count; k++)
            {
                int o = near[k];
                if (o == s) continue;
                Vec2 away = p - EnemyPosition[o];
                float d = away.Length;
                if (d < minDist && d > 1e-4f) p += away * ((minDist - d) * 0.5f / d);
            }
            EnemyPosition[s] = ClampToArena(p, Config.EnemyRadius);
        }
    }

    private void Fire(float dt)
    {
        _fireCooldown -= dt;
        if (_fireCooldown > 0) return;

        int target = FindNearestEnemy(PlayerPosition, Config.FireRange);
        if (target < 0) { _fireCooldown = 0; return; }

        // Lead the target: aim where it will be when the bullet arrives, not where it is.
        Vec2 tp = EnemyPosition[target];
        Vec2 tv = Enemies[target].Velocity;
        float travel = (tp - PlayerPosition).Length / Config.BulletSpeed;
        AimDirection = (tp + tv * travel - PlayerPosition).Normalized();

        int spread = 1 + Math.Min(Wave / 3, 3); // one more bullet every three waves, up to four
        for (int b = 0; b < spread; b++)
        {
            float angle = (b - (spread - 1) * 0.5f) * 0.08f;
            Vec2 dir = AimDirection.Rotate(angle);
            int slot = Bullets.Spawn(new Bullet { Velocity = dir * Config.BulletSpeed, Life = Config.BulletLife });
            if (slot < 0) break;
            BulletPosition[slot] = BulletPrevious[slot] = PlayerPosition + dir * (Config.PlayerRadius + 2);
        }
        _fireCooldown += Config.FireInterval;
    }

    private void MoveBullets(float dt)
    {
        Span<int> near = _neighbours;
        float hitR = Config.BulletRadius + Config.EnemyRadius;

        // Walk backwards: Despawn swaps the last live bullet into the current index.
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            int s = Bullets.ActiveAt(i);
            ref Bullet b = ref Bullets[s];
            b.Life -= dt;
            Vec2 p = BulletPosition[s] + b.Velocity * dt;
            BulletPosition[s] = p;

            if (b.Life <= 0 || p.X < 0 || p.Y < 0 || p.X > Config.Width || p.Y > Config.Height)
            {
                Bullets.Despawn(s);
                continue;
            }

            int count = FindEnemiesNear(p, hitR, near);
            for (int k = 0; k < count; k++)
            {
                int o = near[k];
                if (!Enemies.IsActive(o)) continue; // killed earlier this step, still in the grid
                ref Enemy e = ref Enemies[o];
                e.Health--;
                e.HitFlash = 0.08f;
                if (e.Health <= 0)
                {
                    Enemies.Despawn(o);
                    Kills++;
                    Score += 10 * Math.Max(1, Wave);
                }
                Bullets.Despawn(s);
                break;
            }
        }
    }

    private void CheckPlayerContact(float dt)
    {
        if (PlayerInvulnerable > 0 || Config.Invulnerable) return;
        Span<int> near = _neighbours;
        int count = FindEnemiesNear(PlayerPosition, Config.PlayerRadius + Config.EnemyRadius, near);
        for (int k = 0; k < count; k++)
        {
            if (!Enemies.IsActive(near[k])) continue;
            PlayerHealth = Math.Max(0, PlayerHealth - Config.ContactDamage);
            PlayerInvulnerable = Config.PlayerInvulnerableAfterHit;
            if (PlayerHealth == 0) GameOver = true;
            return;
        }
    }

    private void UpdateWaves(float dt)
    {
        if (!Config.AutoWaves) return;
        WaveTimeLeft -= dt;
        if (Enemies.Count == 0 || WaveTimeLeft <= 0) StartNextWave();
    }

    private void StartNextWave()
    {
        Wave++;
        WaveTimeLeft = Config.WaveSeconds;
        SpawnAtEdges((int)(25 * MathF.Pow(Wave, 1.45f)));
    }

    private Vec2 ClampToArena(Vec2 p, float r) =>
        new(Math.Clamp(p.X, r, Config.Width - r), Math.Clamp(p.Y, r, Config.Height - r));

    /// <summary>A fingerprint of the simulation state, for determinism tests.</summary>
    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(float f) { h ^= (uint)BitConverter.SingleToInt32Bits(f); h *= 1099511628211UL; }
        Mix(PlayerPosition.X); Mix(PlayerPosition.Y);
        for (int i = 0; i < Enemies.Count; i++)
        {
            int s = Enemies.ActiveAt(i);
            Mix(EnemyPosition[s].X); Mix(EnemyPosition[s].Y);
        }
        h ^= (ulong)Score;
        return h;
    }
}
