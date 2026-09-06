using Godot;
using System.Collections.Generic;

// Main only owns the shared world (trees, river, fishing spots, home,
// the console log) and bootstraps NPCs through NpcFactory. Everything
// about running one NPC's cognition loop — its Mind, its Memory, its
// personality, its think/act/fallback turn — lives on NpcAgent instead.
//
// NPCs are entirely data-driven now — see npcs.json (loaded by
// NpcRoster). Every NPC gets the exact same tool menu from Mind
// (gathering, deposit, travel, speak, follow, trade, steal, persuade,
// sleep, wait) regardless of what's in its entry — nothing here or
// anywhere else assigns a role. A backstory leaning toward water is
// just text; whether that shows up as fishing behavior is entirely up
// to the LLM reading it. The player (see CreatePlayer() below) gets the
// same actions too, just decided by input instead of a Mind.
public partial class Main : Node2D
{
    // Must match LogPanel's height in Main.tscn — the camera fit below
    // needs to know how much of the screen the log actually covers.
    private const float UiPanelHeight = 270f;
    private const float CameraMargin = 1.15f; // breathing room around the world's content

    private readonly List<AppleTree> _trees = new();
    private readonly List<FishingSpot> _fishingSpots = new();
    private readonly Dictionary<string, Node2D> _flagpoles = new(); // distant, non-resource landmarks — see MistyMountains
    private readonly List<Node2D> _decor = new(); // everything with IHasVisualBounds, for camera fitting — flagpoles deliberately excluded
    private Home _home;
    private readonly List<string> _logLines = new();
    private RichTextLabel _debugLog;
    private NpcThoughtLogger _thoughtLog;

    // IWorldCharacter, not NpcAgent — this is the SAME list WorldContext
    // hands out, and it holds the player alongside every NPC once
    // CreatePlayer() runs below. Typed generically on purpose: nothing
    // that reads this list (perception, SpeechLog, follow/trade/steal
    // targeting) needs to know or care which entries are LLM-driven.
    private readonly List<IWorldCharacter> _agents = new();

    public override void _Ready()
    {
        BuildWorld();
        _debugLog = GetNode<RichTextLabel>("UI/DebugLog");
        FitCameraToWorld();
        BuildPathGrid();

        MindConfig config = MindConfig.Load();
        _thoughtLog = new NpcThoughtLogger(config.LogNpcThoughts);
        Log($"backend: {config.Provider} ({config.Model})" +
            (config.PureLlmMode ? " [pure LLM mode]" : "") +
            (config.LogNpcThoughts ? $" [logging to {_thoughtLog.LogPath}]" : ""), "6f8068");
        _thoughtLog.Log("*", "RUN_START", $"backend={config.Provider} model={config.Model} pure_llm_mode={config.PureLlmMode}");

        var world = new WorldContext { Trees = _trees, FishingSpots = _fishingSpots, Home = _home, Flagpoles = _flagpoles, Agents = _agents };

        List<NpcDefinition> roster = NpcRoster.Load();
        for (int i = 0; i < roster.Count; i++)
        {
            NpcDefinition def = roster[i];
            Personality personality = def.ToPersonality();

            // % VariantCount, not a direct index — npcs.json can hold
            // more entries than the sprite sheet has color variants
            // without this throwing or needing to grow in lockstep;
            // variants just start repeating. def.Color (still loaded
            // from npcs.json) isn't used for the sprite anymore now that
            // each NPC has real character art instead of a flat-colored
            // square — kept in the data/class shape regardless, in case
            // a future UI element (a nameplate, say) wants a per-NPC
            // accent color independent of which sprite variant they got.
            var agent = NpcFactory.Create(
                this, World(), config, _thoughtLog, Log,
                id: def.Id,
                personality: personality,
                startPosition: new Vector2(def.StartX, def.StartY),
                worldContext: world,
                spriteVariant: i % CharacterSpriteBuilder.VariantCount);
            _agents.Add(agent);

            LogSpawn(def, personality, agent.Actor.Stats);
            agent.Start();
        }

        CreatePlayer(world);
    }

    // Built the same way an NPC is (registered in WorldRegistry, added
    // to the shared Agents list, a fresh NPCActor underneath with its
    // own Inventory/Stats/collision) — the only thing missing is a Mind,
    // because nothing here decides for it. StartScreen (run before this
    // scene loads) is where the player actually typed their name.
    private void CreatePlayer(WorldContext world)
    {
        string name = PlayerStateAutoload()?.PlayerName;
        if (string.IsNullOrWhiteSpace(name))
            name = "Player"; // e.g. running Main.tscn directly during development, skipping StartScreen

        // WorldRegistry.Register() silently overwrites whatever's
        // already at a given key — if the typed name happens to match
        // an NPC's (all NPCs are registered by name before this runs),
        // that NPC would quietly become unreachable by name to every
        // follow/trade/steal/persuade target lookup, itself included in
        // NearbyNpcNames() twice with no way to tell the two apart.
        // Disambiguating here, once, is cheaper than chasing that bug
        // down later.
        if (World().Exists(name))
        {
            string original = name;
            int suffix = 2;
            do { name = $"{original} ({suffix++})"; } while (World().Exists(name));
            Log($"[player] the name '{original}' is already taken by an NPC — using '{name}' instead so targeting stays unambiguous.", "e0c66a");
        }

        var player = new PlayerCharacter { Name = "Player", Position = new Vector2(150, 340) }; // just south of Home, in the open
        AddChild(player);
        player.Initialize(
            name,
            world,
            _thoughtLog,
            Log,
            GetNode<LineEdit>("UI/ChatInput"),
            GetNode("UI/ActionPanel"),
            playerId: "player",
            keys: PlayerCharacter.KeyBindings.WasdAndArrows);

        World().Register("player", player);
        World().Register(player.DisplayName, player);
        _agents.Add(player);

        Log($"[player] {player.DisplayName} joins — {player.Stats.Describe()}", "9fc98a");
        _thoughtLog.Log("player", "SPAWN", $"{player.DisplayName} — {player.Stats.Describe()}");
    }

    private PlayerState PlayerStateAutoload() => GetNodeOrNull<PlayerState>("/root/PlayerState");

    // "Starting params" for one NPC — the resolved trait numbers, not
    // just the archetype id, so a run's log stays self-documenting even
    // if personality_archetypes.json gets edited later. Stats included
    // for the same reason CreatePlayer() logs the player's — both are
    // randomly rolled per spawn, so the actual numbers only exist here.
    private void LogSpawn(NpcDefinition def, Personality p, CharacterStats stats)
    {
        string line = $"[{def.Id}] {p.Name} — archetype '{def.Archetype}' " +
            $"(O={p.Openness:0.00} C={p.Conscientiousness:0.00} E={p.Extraversion:0.00} " +
            $"A={p.Agreeableness:0.00} N={p.Neuroticism:0.00}, temp={p.Temperature:0.00}), " +
            $"{stats.Describe()}, " +
            $"start=({def.StartX:0},{def.StartY:0}) — \"{p.Backstory}\"";
        Log(line, "9fc98a");
        _thoughtLog.Log(def.Id, "SPAWN", line);
    }

    private void BuildWorld()
    {
        BuildGround();

        _home = new Home { Name = "Home", Position = new Vector2(150, 220) };
        AddChild(_home);
        World().Register("home", _home);
        _decor.Add(_home);

        Vector2[] treePositions = { new(600, 120), new(800, 180), new(680, 380) };
        for (int i = 0; i < treePositions.Length; i++)
        {
            var tree = new AppleTree { Name = $"Tree{i}", Position = treePositions[i], AppleCount = 3 };
            AddChild(tree);
            World().Register($"tree_{i}", tree);
            _trees.Add(tree);
            _decor.Add(tree);
        }

        var river = new River { Name = "River", Position = new Vector2(560, 620) };
        AddChild(river);
        _decor.Add(river);

        // Placed exactly on the river's actual (wavy) centerline at
        // each x, rather than a fixed y — a straight-line guess would
        // land off the water at some points along a sine curve.
        float[] fishingX = { 260f, 860f };
        foreach (float x in fishingX)
        {
            var spot = new FishingSpot
            {
                Name = $"FishingSpot{_fishingSpots.Count}",
                Position = new Vector2(x, river.GetCenterlineWorldY(x)),
                FishCount = 3,
            };
            AddChild(spot);
            World().Register($"fish_{_fishingSpots.Count}", spot);
            _fishingSpots.Add(spot);
            _decor.Add(spot);
        }

        // The frontier: forest, foothills, and a genuinely deep river
        // crossing between the village and misty mountains. None of
        // these get a WorldRegistry id or a tool-schema entry — they're
        // not destinations, just terrain. "travel" never uses PathGrid
        // (the path to a flagpole is unknown by design), so the only
        // thing that routes an NPC around these is reactive physics
        // collision — bumping into them and sliding past, discovering
        // the obstacle course the same way the NPC would if it really
        // didn't know the way. Also deliberately NOT in _decor, same
        // reasoning as the mountains: frontier terrain shouldn't be
        // what the camera auto-fits the cozy village view around.
        AddChild(new ForestPatch { Name = "Forest", Position = new Vector2(600, -100) });
        AddChild(new Foothills { Name = "Foothills", Position = new Vector2(820, -220) });
        AddChild(new RiverCrossing { Name = "DeepRiver", Position = new Vector2(650, -300) });

        // Position is a best-effort guess at landing near the top edge
        // of whatever frame the village's own bounds produce, moved
        // further out than before to leave real room for the obstacle
        // course above — unverified without being able to render this.
        var mountains = new MistyMountains { Name = "MistyMountains", Position = new Vector2(700, -380) };
        AddChild(mountains);
        World().Register("misty_mountains", mountains);
        _flagpoles["misty_mountains"] = mountains;
    }

    // Sizes and centers Camera2D around whatever the world actually
    // contains, instead of hand-computed position/zoom constants going
    // stale every time the layout changes — every world object with
    // IHasVisualBounds contributes to the union, and this fits it with
    // margin, accounting for the log panel's footprint at the bottom.
    private void FitCameraToWorld()
    {
        Rect2? bounds = null;
        foreach (Node2D node in _decor)
        {
            if (node is not IHasVisualBounds hasBounds)
                continue;
            Rect2 local = hasBounds.GetLocalBounds();
            var worldRect = new Rect2(local.Position + node.Position, local.Size);
            bounds = bounds.HasValue ? bounds.Value.Merge(worldRect) : worldRect;
        }
        if (!bounds.HasValue)
            return;

        Rect2 b = bounds.Value;
        Vector2 viewportSize = GetViewportRect().Size;
        float availableHeight = Mathf.Max(viewportSize.Y - UiPanelHeight, 1f);

        float zoomX = (b.Size.X * CameraMargin) / viewportSize.X;
        float zoomY = (b.Size.Y * CameraMargin) / availableHeight;
        float zoom = Mathf.Max(Mathf.Max(zoomX, zoomY), 0.3f);

        var camera = GetNode<Camera2D>("Camera2D");
        camera.Zoom = new Vector2(zoom, zoom);

        // Bias the vertical center upward so content centers in the
        // AVAILABLE (non-UI-covered) part of the screen, not the full
        // viewport — otherwise content centered in the full viewport
        // would have its bottom edge sit behind the log panel.
        Vector2 contentCenter = b.Position + b.Size / 2f;
        camera.Position = new Vector2(contentCenter.X, contentCenter.Y + (UiPanelHeight / 2f) * zoom);
    }

    // Covers the resource area (home, trees, river) with real margin
    // for future additions there — but stops short of the frontier
    // (y less than -50): the forest/foothills/river-crossing/mountains
    // sit at y -100 and beyond specifically so this rectangle excludes
    // them without needing extra filtering logic. That's
    // belt-and-suspenders with them not being in _decor at all — either
    // exclusion alone would already keep them out of A*, matching
    // "travel" never using the grid regardless.
    private static readonly Rect2 PathGridArea = new(-50, -50, 1250, 850);

    // One tiled grass Sprite2D covering the same area PathGridArea does
    // (same "resource area, not the frontier" reasoning as its comment
    // above) — a single node, not a grid of hundreds of small ones:
    // Sprite2D's RegionRect, given a region LARGER than the source
    // texture plus TextureRepeat.Enabled, samples the 16×16 tile
    // repeatedly to fill it, and Scale then blows the whole thing up
    // uniformly so each repeat reads at GroundTileWorldSize on screen
    // instead of the source's native 16px. Added first, before anything
    // else in BuildWorld(), so every other object's default sibling draw
    // order puts it on top — this is the only thing in the scene that
    // actually needs to be "the floor."
    private const float GroundTileWorldSize = 32f;

    private void BuildGround()
    {
        float s = GroundTileWorldSize / 16f;
        var ground = new Sprite2D
        {
            Name = "Ground",
            Texture = GD.Load<Texture2D>("res://assets/world/grass.png"),
            Centered = false,
            Position = PathGridArea.Position,
            RegionEnabled = true,
            RegionRect = new Rect2(0, 0, PathGridArea.Size.X / s, PathGridArea.Size.Y / s),
            Scale = new Vector2(s, s),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
        };
        AddChild(ground);
    }

    private void BuildPathGrid()
    {
        var obstacles = new List<(Vector2 Center, float Radius)>();
        foreach (Node2D node in _decor)
            if (node is IObstacle obstacle)
                foreach ((Vector2 offset, float radius) in obstacle.GetObstacleCircles())
                    obstacles.Add((node.Position + offset, radius));

        PathGrid.Build(PathGridArea, obstacles);
    }

    private WorldRegistry World() => GetNode<WorldRegistry>("/root/World");

    private void Log(string line, string color = "d8ddd0")
    {
        // escape literal brackets so LLM text can never be read as a bbcode tag
        string safe = line.Replace("[", "[lb]");
        if (safe.Length > 200)
            safe = safe.Substring(0, 197) + "...";
        _logLines.Add($"[color=#{color}]{safe}[/color]");
        if (_logLines.Count > 40)
            _logLines.RemoveAt(0);
        _debugLog.Text = string.Join("\n", _logLines);
    }
}
