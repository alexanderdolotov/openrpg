using Godot;
using System.Collections.Generic;

// Main only owns the shared world (trees, river, fishing spots, home,
// the console log) and bootstraps NPCs through NpcFactory. Everything
// about running one NPC's cognition loop — its Mind, its Memory, its
// personality, its think/act/fallback turn — lives on NpcAgent instead.
//
// NPCs are entirely data-driven now — see npcs.json (loaded by
// NpcRoster). Every NPC gets the exact same tool menu from Mind
// (gathering, deposit, travel, speak, follow, trade, steal, sleep, wait)
// regardless of what's in its entry — nothing here or
// anywhere else assigns a role. A backstory leaning toward water is
// just text; whether that shows up as fishing behavior is entirely up
// to the LLM reading it. The player (see CreatePlayer() below) gets the
// same actions too, just decided by input instead of a Mind.
public partial class Main : Node2D
{
    // How zoomed in the follow camera sits. CORRECTED, verified with an
    // actual measurement (mapping two points 100 world-units apart
    // through Camera2D.get_canvas_transform() and checking the resulting
    // screen-pixel distance) rather than trusted from memory again:
    // Zoom ABOVE 1 zooms IN (objects appear BIGGER — at zoom 2, 100
    // world units measured 200 screen px); Zoom BELOW 1 zooms OUT
    // (smaller — at zoom 0.4, 100 world units measured only 40 screen
    // px). Every previous pass here (0.65 -> 0.55 -> 0.5 -> 0.4) had
    // this backwards and was zooming OUT further each time, which is
    // exactly why it kept feeling more zoomed out despite being asked
    // for the opposite. This is a real correction, not another nudge —
    // still a starting value to react to, but now moving the right way.
    private const float FollowZoom = 1.6f;

    private readonly List<AppleTree> _trees = new();
    private readonly List<FishingSpot> _fishingSpots = new();
    private readonly List<GatherableFoliage> _pineTrees = new();
    private readonly List<GatherableFoliage> _berryBushes = new();
    private readonly List<GatherableFoliage> _grassPatches = new(); // see WorldContext.GrassPatches for why this exists

    // The subset of _fishingSpots placed directly along the river at
    // boot — the ones RelocateRiverFish() periodically teleports to a
    // fresh random point and refills (see BuildRiverFishTimer). NOT the
    // same as exploration-generated standalone spots elsewhere in
    // _fishingSpots, which stay put per their own established design —
    // tracked separately so relocation only ever touches this subset.
    private readonly List<FishingSpot> _riverFishingSpots = new();
    private River _river;

    private readonly List<Stick> _sticks = new();

    // Every living wild animal — the SAME list WorldContext.Animals
    // hands out (see BuildWorld's own comment on _agents for why this
    // reference-sharing pattern already exists). Main is the only thing
    // that ever adds/removes from it (spawning, Animal.Died) — a
    // species' own DecideBehavior() only ever reads it.
    private readonly List<Animal> _animals = new();

    private readonly Dictionary<string, Node2D> _flagpoles = new(); // distant, non-resource landmarks — see MistyMountains
    // Everything that can obstruct movement (via IObstacle, feeding
    // BuildPathGrid()) gets registered here. Also still the complete
    // set of IHasVisualBounds implementers, though nothing reads that
    // interface anymore now that the camera follows the player instead
    // of fitting itself to the whole world — left in place on each
    // object rather than ripped out, since it's harmless, accurate
    // metadata about each object's visual footprint that something else
    // (a minimap, say) could reasonably want later.
    private readonly List<Node2D> _decor = new();

    // A YSortEnabled container — Godot's built-in mechanism for exactly
    // "whoever's lower on screen draws in front," re-evaluated every
    // frame from each child's own Y position. This is what makes walking
    // above a tree correctly go behind its canopy and walking below it
    // correctly stay in front, instead of a fixed draw order where
    // characters (added to the tree after every tree exists) always won
    // regardless of position.
    //
    // Every character (NPC or player) is a DIRECT child of this layer —
    // NPCActor used to sit one level deeper, under NpcAgent (a plain
    // Node wrapper for the cognition loop), on the theory that Y-sort
    // would compose through a non-CanvasItem wrapper transparently the
    // same way Node2D transform inheritance does. In practice NPCs
    // rendered wrong against trees (in front of canopy the player
    // correctly went behind) while sharing otherwise-identical sprite/
    // anchor code with PlayerCharacter — the one structural difference
    // was that extra level of nesting. NpcFactory now adds NPCActor
    // directly here, as a sibling of NpcAgent rather than its child, so
    // there's one uniform tree depth for every character and nothing
    // left to reason about there. Scoped to objects with real
    // "height" a character can walk in front of or behind — Home, trees,
    // every character. Deliberately NOT water (River, FishingSpot) or
    // the frontier decorations (mountains/forest/foothills/river-
    // crossing) — those are flat terrain/backdrop, always rendered
    // behind this whole layer via plain sibling order in BuildWorld()
    // (added to Main before this layer even exists), not Y-sorted
    // against it. They briefly WERE Y-sorted, which was the actual bug
    // behind a character rendering under the river/mountains: sibling
    // order at the Main level (not Y-sort, which only reorders within
    // one YSortEnabled parent) is what decided draw order between this
    // whole layer and its OWN Main-level siblings, and back then those
    // terrain pieces happened to be added after this layer, so they won.
    private Node2D _worldLayer;

    private Home _home;
    private FirePit _firePit;
    private RichTextLabel _debugLog;
    private NpcThoughtLogger _thoughtLog;

    // The player's own reference — needed for RefreshNpcFilterBar's
    // "who's actually near ME" distance check below. Everything else
    // in this file reaches the player only indirectly through _agents
    // (typed as the generic IWorldCharacter), which is fine for
    // perception/targeting but not for "the player specifically."
    private PlayerCharacter _player;

    // "Add filter buttons to the top [of the log] for nearby NPCs...
    // once I'm not in their vicinity anymore, remove the filter
    // automatically." _npcFilterButtons tracks which NPCs currently
    // have a button showing (one HBoxContainer child each, keyed by
    // DisplayName — the exact string every one of their own log lines
    // starts with, see ExtractSpeaker); RefreshNpcFilterBar adds/
    // removes them as the player walks in and out of SpeechLog.
    // HearingRadius — "nearby" meaning the same thing here as it does
    // for actually hearing someone speak. See Log()/SetActiveLogFilter/
    // RebuildDebugLogDisplay for the filtering itself.
    private HBoxContainer _npcFilterBar;
    private readonly Dictionary<string, Button> _npcFilterButtons = new();
    private float _npcFilterRefreshTimer;
    private const float NpcFilterRefreshInterval = 0.5f; // real seconds between rescans — a handful of NPCs, no need to check every physics frame

    // Kept as a field (not just a _Ready()-local) so GenerateContentAt()
    // can bump ContentVersion whenever exploration-driven generation
    // adds a tree or fishing spot after boot — see WorldContext and
    // NpcAgent's TreeIds()/FishingSpotIds() caching.
    private WorldContext _world;

    // IWorldCharacter, not NpcAgent — this is the SAME list WorldContext
    // hands out, and it holds the player alongside every NPC once
    // CreatePlayer() runs below. Typed generically on purpose: nothing
    // that reads this list (perception, SpeechLog, follow/trade/steal
    // targeting) needs to know or care which entries are LLM-driven.
    private readonly List<IWorldCharacter> _agents = new();

    // One-shot: DebugLog's own boot-time spawn/backend lines (logged
    // during THIS class's _Ready(), below) never actually end up
    // scrolled into view — verified headlessly that RichTextLabel's
    // layout geometry (MaxValue/Page) is already correct by frame 0,
    // but a ScrollToLine() call made DURING _Ready() itself silently
    // doesn't stick (something in Godot's own internal RichTextLabel
    // setup resets scroll position after user _Ready() calls run, and
    // it stays stuck at the top from then on — Log()'s own "was already
    // at the bottom" check has nothing to recover from, since it never
    // WAS at the bottom to begin with). _Process() genuinely only ever
    // runs once Godot's own setup has fully settled, unlike _Ready(),
    // so catching up here — once, then never again — establishes a
    // correct starting position for Log()'s ongoing logic to work from.
    private bool _logScrollCaughtUp = false;

    public override void _Process(double delta)
    {
        if (!_logScrollCaughtUp)
        {
            _logScrollCaughtUp = true;
            _debugLog.ScrollToLine(_debugLog.GetLineCount());
        }

        _npcFilterRefreshTimer += (float)delta;
        if (_npcFilterRefreshTimer >= NpcFilterRefreshInterval)
        {
            _npcFilterRefreshTimer = 0f;
            RefreshNpcFilterBar();
        }
    }

    public override void _Ready()
    {
        // Always — not the default Inherit — specifically so this node's
        // own _UnhandledKeyInput (the Space shortcut below) and the UI
        // CanvasLayer's buttons/log/chat (children of Main, still on
        // Inherit, so they follow Main's own effective mode) keep
        // working while GetTree().Paused is true. _worldLayer gets its
        // own explicit Pausable override right where it's created
        // (BuildWorld()) specifically to still freeze despite that —
        // Godot's ProcessMode inheritance stops at the first descendant
        // with a non-Inherit mode of its own, so Main being Always
        // doesn't leak down past that override.
        ProcessMode = ProcessModeEnum.Always;

        // Every one of these is static, meaning it lives outside the
        // scene tree "Restart Game" (SettingsPanel's own RestartButton)
        // reloads — without resetting them here, a restarted session
        // would silently inherit stale state from whatever session ran
        // before it in this same process (first boot has nothing to
        // clear yet, so this is a harmless no-op there). Found auditing
        // after the restart button existed to actually expose it — see
        // each Reset()'s own comment for what specifically broke.
        WorldExploration.Reset();
        SpeechLog.Reset();
        WorldEventLog.Reset();

        BuildWorld();
        BuildLighting();
        _debugLog = GetNode<RichTextLabel>("UI/DebugLog");
        _npcFilterBar = GetNode<HBoxContainer>("UI/NpcFilterBar");
        BuildPathGrid();

        var pauseButton = GetNode<Button>("UI/PauseButton");
        pauseButton.Pressed += TogglePause;
        BuildSettingsMenu();

        MindConfig config = MindConfig.Load();
        GameSettings.PermadeathEnabled = config.PermadeathEnabled;
        GameSettings.LogLevel = config.LogLevel;
        _thoughtLog = new NpcThoughtLogger(config.LogNpcThoughts);
        Log($"backend: {config.Provider} ({config.Model})" +
            (config.PureLlmMode ? " [pure LLM mode]" : "") +
            (config.LogNpcThoughts ? $" [logging to {_thoughtLog.LogPath}]" : ""), "6f8068");
        _thoughtLog.Log("*", "RUN_START", $"backend={config.Provider} model={config.Model} pure_llm_mode={config.PureLlmMode}");

        _world = new WorldContext { Trees = _trees, FishingSpots = _fishingSpots, PineTrees = _pineTrees, BerryBushes = _berryBushes, GrassPatches = _grassPatches, Sticks = _sticks, Home = _home, FirePit = _firePit, Flagpoles = _flagpoles, Agents = _agents, Animals = _animals };
        WorldContext world = _world;

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
                _worldLayer, World(), config, _thoughtLog, Log,
                id: def.Id,
                personality: personality,
                startPosition: new Vector2(def.StartX, def.StartY),
                worldContext: world,
                spriteVariant: i % CharacterSpriteBuilder.VariantCount);
            _agents.Add(agent);
            agent.Actor.Downed += () => OnCharacterDowned(agent.Actor, agent.Personality.Name);

            LogSpawn(def, personality, agent.Actor.Stats);
            agent.Start();
        }

        PlayerCharacter player = CreatePlayer(world);
        _player = player;
        AttachFollowCamera(player);
        player.Downed += () => OnCharacterDowned(player, player.DisplayName);

        SpawnInitialAnimals();
        BuildAnimalPopulationTimer();
    }

    // Only ever fires under GameSettings.PermadeathEnabled — see
    // NPCActor.ReceiveDamage(). Unregisters the character so nothing
    // else can target them anymore (follow/trade/steal, or a fresh
    // "nearest human" query from an Animal), but deliberately leaves
    // the body itself in the world, still visible (NPCActor's own
    // permanent 💀-and-lying-down visual) rather than freeing the node
    // — losing an NPC this way is meant to be a real, visible story
    // event, not a silent disappearance.
    private void OnCharacterDowned(NPCActor actor, string displayName)
    {
        _agents.RemoveAll(a => a.DisplayName == displayName);
        World().Unregister(displayName);
        Log($"[world] {displayName} has fallen.", "e0876b");
        _thoughtLog?.Log(displayName, "DOWNED", $"{displayName} has fallen (permadeath).");

        if (actor is PlayerCharacter)
        {
            GetTree().Paused = true;
            var pauseButton = GetNode<Button>("UI/PauseButton");
            pauseButton.Text = "Restart";
            pauseButton.Pressed -= TogglePause;
            pauseButton.Pressed += RestartGame;
            Log("[world] You have died. Click Restart (or the Pause button) to play again.", "e0876b");
        }
    }

    // Closes the current run's thought-log file handle first — Main's
    // own _Ready() hands _thoughtLog a brand new NpcThoughtLogger once
    // ReloadCurrentScene() re-runs it, and without this the previous
    // instance's open StreamWriter has no caller left to close it,
    // leaking the file handle until the GC eventually finalizes it.
    private void RestartGame()
    {
        _thoughtLog?.Close();
        GetTree().ReloadCurrentScene();
    }

    // Built the same way an NPC is (registered in WorldRegistry, added
    // to the shared Agents list, a fresh NPCActor underneath with its
    // own Inventory/Stats/collision) — the only thing missing is a Mind,
    // because nothing here decides for it. StartScreen (run before this
    // scene loads) is where the player actually typed their name.
    private PlayerCharacter CreatePlayer(WorldContext world)
    {
        string name = PlayerStateAutoload()?.PlayerName;
        if (string.IsNullOrWhiteSpace(name))
            name = "Player"; // e.g. running Main.tscn directly during development, skipping StartScreen

        // WorldRegistry.Register() silently overwrites whatever's
        // already at a given key — if the typed name happens to match
        // an NPC's (all NPCs are registered by name before this runs),
        // that NPC would quietly become unreachable by name to every
        // follow/trade/steal target lookup, itself included in
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
        _worldLayer.AddChild(player); // same YSortEnabled layer as trees/Home/NPCs — see its field comment
        player.Initialize(
            name,
            world,
            _thoughtLog,
            Log,
            GetNode<LineEdit>("UI/ChatInput"),
            GetNode("UI/ActionPanel"),
            GetNode("UI/VitalsPanel"),
            playerId: "player",
            keys: PlayerCharacter.KeyBindings.WasdAndArrows);

        World().Register("player", player);
        World().Register(player.DisplayName, player);
        _agents.Add(player);

        Log($"[player] {player.DisplayName} joins — {player.Stats.Describe()}", "9fc98a");
        _thoughtLog.Log("player", "SPAWN", $"{player.DisplayName} — {player.Stats.Describe()}");
        return player;
    }

    // Reparents the scene's own Camera2D onto the player — a Camera2D
    // simply tracks its parent's position every frame with zero extra
    // code, which is what makes this "follow" rather than the old
    // "recompute a static fit once at boot" approach. Has to happen
    // after CreatePlayer(), since there's nothing to reparent onto
    // before the player exists.
    private void AttachFollowCamera(PlayerCharacter player)
    {
        var camera = GetNode<Camera2D>("Camera2D");
        camera.Reparent(player, keepGlobalTransform: false);
        camera.Position = Vector2.Zero;
        camera.Zoom = new Vector2(FollowZoom, FollowZoom);
        camera.PositionSmoothingEnabled = true;
        camera.PositionSmoothingSpeed = 8f;
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

        // Flat terrain/backdrop — water and distant scenery, never a
        // thing a character reads as "in front of" or "behind." Added
        // to Main directly, and specifically BEFORE _worldLayer exists,
        // so plain Node sibling order (later added = drawn on top) puts
        // the whole Y-sorted layer — Home, trees, every character — in
        // front of all of it unconditionally. Y-sorting these instead
        // would be the wrong tool anyway: a Y-sort compares POSITIONS,
        // and it briefly did exactly that here, which is what let the
        // river and mountains — both added as later siblings than
        // _worldLayer at the time — win the sibling-order tiebreak and
        // render on top of it regardless of anyone's actual position.
        // Length/Waves both grown from the original 1050/2 — "stretches
        // quite far" — keeping roughly the same waves-per-length ratio
        // so the meander still reads at a similar visual frequency
        // rather than a few waves stretched thin across the new length.
        // "Make the village river much much longer" — 2600 (itself
        // already grown once from an original 1050) roughly tripled.
        // Bounds-checked against WorldExploration.MaxMapBounds (x:
        // -2000..3500) so the ribbon never extends past the explorable/
        // rendered ground: centered at x=560, a Length of 5000 spans
        // -1940..2940, comfortably inside on both sides. Waves scaled to
        // keep the same ~520-world-units-per-full-wave rhythm the
        // previous 2600/5 ratio already established (5000/520 ≈ 9.6,
        // rounded), and Segments scaled right alongside Length (not left
        // fixed at 48) — BuildRibbon() samples Segments+1 points across
        // the WHOLE curve regardless of how many waves that spans, so a
        // much longer river at the old segment count would resample
        // each individual wave far more coarsely and start looking
        // faceted rather than smooth.
        var river = new River { Name = "River", Position = new Vector2(560, 620), Length = 5000f, Waves = 10f, Segments = 92 };
        AddChild(river);
        _decor.Add(river);
        _river = river;

        // Placed exactly on the river's actual (wavy) centerline at
        // each x, rather than a fixed y — a straight-line guess would
        // land off the water at some points along a sine curve. Same
        // "always behind, added before _worldLayer exists" terrain
        // treatment as River itself. These five specifically are the
        // ones RelocateRiverFish() periodically teleports to a fresh
        // random point along the (now much longer) river — tracked in
        // _riverFishingSpots for that, separate from any exploration-
        // generated standalone spot elsewhere, which stays put.
        float[] fishingX = { -700f, -200f, 400f, 1000f, 1600f };
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
            _riverFishingSpots.Add(spot);
            _decor.Add(spot);
        }

        // These terrain pieces are added to _decor (below), unlike
        // when this was first written — each implements IObstacle
        // (real physics collision already worked regardless, via their
        // own CollisionShape2D) but BuildPathGrid() only ever reads
        // _decor, so an NPC's own A*-ish pathing had no idea any of
        // these were actually solid — it could path an NPC straight at
        // the mountains and just let real collision awkwardly shove
        // them off course, rather than routing around in the first
        // place. Found while adding TooCloseToExisting/NudgeToClearSpot
        // below for a different bug (a grass patch placed directly on
        // top of the fire pit) — that check is only as complete as
        // _decor is, so this was the same gap wearing two different
        // hats.
        var forest = new ForestPatch { Name = "Forest", Position = new Vector2(600, -100) };
        AddChild(forest);
        _decor.Add(forest);
        var foothills = new Foothills { Name = "Foothills", Position = new Vector2(820, -220) };
        AddChild(foothills);
        _decor.Add(foothills);

        // Position is a best-effort guess at landing near the top edge
        // of whatever frame the village's own bounds produce, moved
        // further out than before to leave real room for the obstacle
        // course above — unverified without being able to render this.
        // Constructed (and added to _decor) here, but not actually
        // AddChild'd into the scene tree until AFTER the lake below —
        // _decor only drives the placement/pathing CHECKS (plain C#,
        // no tree dependency), while actual draw order comes from real
        // scene-tree child order. Doing it in this split order is what
        // lets NudgeToClearSpot see the mountains' own flank circles
        // when it places the lake next, while still leaving mountains
        // as the LATER sibling — drawn on top of the lake, the correct
        // way around for a solid landmark that should occlude water
        // behind it, not the other way around.
        var mountains = new MistyMountains { Name = "MistyMountains", Position = new Vector2(700, -380) };
        _decor.Add(mountains);

        // A round lake, not a straight river-crossing band — "the misty
        // mountain and random rivers are not nice and wavy like the
        // village river... make them more like lakes instead" (see
        // Lake's own header for the full reasoning, including why it's
        // premade art now rather than a procedural shape).
        //
        // (650, -300) — directly between Forest, Foothills, AND Misty
        // Mountains — was never actually clear ground for something
        // this size (obstacle radius 126): fine for the old, much
        // thinner RiverCrossing band that used to sit here, not a big
        // round lake. NudgeToClearSpot alone couldn't fix that
        // gracefully — the only way out of a spot boxed in by three
        // large neighbors at once was a long straight-line push, which
        // just traded "overlapping the mountains" for "floating in the
        // sky past their peaks." (1250, -300) is open ground east of
        // the whole cluster instead — same distance south as the
        // original pick, comfortably clear of Foothills and every
        // Misty Mountains flank without needing to travel anywhere.
        var mountainLake = new Lake { Name = "MountainLake", Position = NudgeToClearSpot(new Vector2(1250, -300), 140f * 0.9f), Radius = 140f };
        AddChild(mountainLake);
        _decor.Add(mountainLake);

        AddChild(mountains);
        World().Register("misty_mountains", mountains);
        _flagpoles["misty_mountains"] = mountains;

        // ProcessMode explicitly set, not left on the default Inherit —
        // Main itself is ProcessModeEnum.Always (see _Ready()), and
        // without this explicit override, _worldLayer (and everything
        // under it: Home, trees, every NPC, the player) would inherit
        // that and never actually freeze on pause. This is what makes
        // "the world pauses, the UI doesn't" a real split instead of an
        // accident of tree order.
        _worldLayer = new Node2D { Name = "WorldLayer", YSortEnabled = true, ProcessMode = ProcessModeEnum.Pausable };
        AddChild(_worldLayer);

        _home = new Home { Name = "Home", Position = new Vector2(150, 220) };
        _worldLayer.AddChild(_home);
        World().Register("home", _home);
        _decor.Add(_home);

        // "A fire pit near the house" — just south of it, close enough
        // to read as part of the same little homestead.
        _firePit = new FirePit { Name = "FirePit", Position = new Vector2(150, 300) };
        _worldLayer.AddChild(_firePit);
        World().Register("firepit", _firePit);
        _decor.Add(_firePit);

        Vector2[] treePositions = { new(600, 120), new(800, 180), new(680, 380) };
        for (int i = 0; i < treePositions.Length; i++)
        {
            var tree = new AppleTree { Name = $"Tree{i}", Position = treePositions[i], AppleCount = 3 };
            _worldLayer.AddChild(tree);
            World().Register($"tree_{i}", tree);
            _trees.Add(tree);
            _decor.Add(tree);
        }

        // Pine trees (gatherable — pinecones), same Y-sorted/collidable
        // treatment as apple trees, just a different item and sprite.
        Vector2[] pinePositions = { new(250, 450), new(900, 480) };
        foreach (Vector2 pos in pinePositions)
        {
            int i = _pineTrees.Count;
            var pine = new GatherableFoliage
            {
                Name = $"Pine{i}", Position = pos, Count = 3,
                ActionId = "gather_pinecone", ItemName = "pinecone",
                TexturePath = "res://assets/world/pine.png", SpriteScale = 4.5f, TrunkRadius = 12f,
            };
            _worldLayer.AddChild(pine);
            World().Register($"pine_{i}", pine);
            _pineTrees.Add(pine);
            _decor.Add(pine);
        }

        // Berry bushes (gatherable) — TWO of each kind to start now (was
        // one), same shared sprite with a per-type Modulate tint (see
        // GatherableFoliage's own header for why there's only one
        // source sprite for all three). "More berry bushes for
        // everyone" — the original 3 total, shared across every human
        // AND every animal on the map, turned out to be genuine scarcity
        // once population grew; see GrassPatches just below for the
        // other half of that same fix.
        (Vector2 Pos, string Item, Color Tint)[] berries =
        {
            (new Vector2(700, 250), "blueberry", new Color(0.5f, 0.9f, 2.0f)),
            (new Vector2(850, 420), "blackberry", new Color(0.55f, 0.75f, 1.5f)),
            (new Vector2(300, 380), "raspberry", new Color(1.2f, 0.6f, 0.6f)),
            (new Vector2(550, 480), "blueberry", new Color(0.5f, 0.9f, 2.0f)),
            (new Vector2(950, 250), "blackberry", new Color(0.55f, 0.75f, 1.5f)),
            (new Vector2(180, 480), "raspberry", new Color(1.2f, 0.6f, 0.6f)),
        };
        foreach ((Vector2 pos, string item, Color tint) in berries)
        {
            int i = _berryBushes.Count;
            const float trunkRadius = 8f;
            var bush = new GatherableFoliage
            {
                Name = $"Berry{i}", Position = NudgeToClearSpot(pos, trunkRadius), Count = 3,
                ActionId = "gather_berry", ItemName = item,
                TexturePath = "res://assets/world/bush_berry.png", SpriteScale = 2.5f, TrunkRadius = trunkRadius,
                Tint = tint,
                RegenSeconds = 60f, // picked/grazed clean, back to full ~1 minute after the last berry taken — same as grass
            };
            _worldLayer.AddChild(bush);
            World().Register($"berry_{i}", bush);
            _berryBushes.Add(bush);
            _decor.Add(bush);
        }

        // Grass patches — animal food only (ActionId "" — see
        // WorldContext.GrassPatches's own header for why), common and
        // scattered widely so a rabbit wandering almost anywhere on the
        // starting map has SOMETHING within its detection radius, not
        // just the handful of actual berry bushes. grass_patch.png (a
        // premade round grass mound — see assets/CREDITS.md) rather than
        // bush_plain.png tinted green, which used to make these read as
        // round bush blobs instead of ground-level grass. Walkable —
        // ground cover, not an obstacle; every character should walk
        // straight over it, same as GenerateGrassPatch's later-grown
        // patches (this is just the starting seed batch, same shape).
        //
        // "You put a grass patch right on top of a fire — check if
        // anything existing is already there before placing anything,
        // same goes for all objects." One of these ((150, 300)) is
        // EXACTLY the fire pit's own position — that's the actual bug,
        // not a hypothetical one. Every position below now goes through
        // NudgeToClearSpot rather than being trusted as hand-picked-and-
        // therefore-safe, which is what let that ship in the first
        // place — a real runtime check against every already-placed
        // object's actual footprint, not another guess at coordinates
        // that look clear on paper.
        Vector2[] grassPositions =
        {
            new(450, 200), new(620, 400), new(150, 300), new(780, 550),
            new(1000, 350), new(350, 550), new(880, 180), new(50, 420),
        };
        foreach (Vector2 pos in grassPositions)
            SpawnGrassPatch(pos);

        // Standalone — oak/bare tree/plain bush, nothing to gather, just
        // scenery to walk around (same as Foothills/ForestPatch aren't
        // registered in WorldRegistry either — nothing ever needs to
        // look one of these up by id).
        var oak0 = new DecorativeFoliage { Name = "Oak0", Position = new Vector2(350, 250), TexturePath = "res://assets/world/oak.png", SpriteScale = 4.5f, TrunkRadius = 12f };
        var oak1 = new DecorativeFoliage { Name = "Oak1", Position = new Vector2(950, 300), TexturePath = "res://assets/world/oak.png", SpriteScale = 4.5f, TrunkRadius = 12f };
        var bareTree = new DecorativeFoliage { Name = "BareTree0", Position = new Vector2(500, 550), TexturePath = "res://assets/world/bare_tree.png", SpriteScale = 4.5f, TrunkRadius = 10f };
        var plainBush = new DecorativeFoliage { Name = "PlainBush0", Position = new Vector2(400, 150), TexturePath = "res://assets/world/bush_plain.png", SpriteScale = 2.5f, TrunkRadius = 8f };
        foreach (DecorativeFoliage foliage in new[] { oak0, oak1, bareTree, plainBush })
        {
            _worldLayer.AddChild(foliage);
            _decor.Add(foliage);
        }

        // Sticks — "for now, place sticks that can be picked up as a
        // basic weapon." Flat ground clutter, same "always behind"
        // treatment as FishingSpot (added to Main directly, not
        // _worldLayer — nothing about lying on the ground has real
        // visual height a character walks in front of or behind).
        Vector2[] stickPositions = { new(450, 500), new(750, 150), new(200, 600) };
        foreach (Vector2 pos in stickPositions)
        {
            var stick = new Stick { Name = $"Stick{_sticks.Count}", Position = pos };
            string stickId = $"stick_{_sticks.Count}";
            AddChild(stick);
            World().Register(stickId, stick);
            stick.WorldId = stickId;
            _sticks.Add(stick);
            // Picked up (or otherwise freed) -> no longer a valid
            // target; NpcAgent's cached StickIds() needs to know its
            // list is stale, same ContentVersion mechanism new trees
            // already bump.
            stick.TreeExiting += () =>
            {
                _sticks.Remove(stick);
                _world.ContentVersion++;
            };
        }

        // Everything hand-placed above counts as "already known" —
        // without this, the instant anyone takes a single step,
        // WorldExploration would read their own starting region as
        // newly discovered and immediately start layering random
        // generated content on top of the curated village. Deliberately
        // NOT GroundArea (that's WorldExploration.MaxMapBounds now, the
        // full playable extent — marking THAT explored at boot would
        // mean nothing ever counts as newly-discovered and generation
        // could never fire at all) — StartingVillageArea instead, the
        // actual hand-placed footprint.
        WorldExploration.MarkExplored(StartingVillageArea);
        BuildExplorationTimer();
        BuildRiverFishTimer();
    }

    // "Fish that appear and reappear at random spots in the river every
    // few minutes" — periodically teleports each of _riverFishingSpots
    // to a fresh random point along the (now much longer) river and
    // refills it, rather than each just sitting in its one starting
    // spot for the whole session. A child of _worldLayer for the same
    // pause-safety reason as BuildExplorationTimer's own timer.
    private const float RiverFishRelocateSeconds = 150f; // "every few minutes"

    private void BuildRiverFishTimer()
    {
        var timer = new Timer { WaitTime = RiverFishRelocateSeconds, Autostart = true };
        _worldLayer.AddChild(timer);
        timer.Timeout += RelocateRiverFish;
    }

    private void RelocateRiverFish()
    {
        // Keeps clear of the river's own two ends (a fish spot exactly
        // at x=±Length/2 would sit right at the bank's tapered tip).
        float margin = 80f;
        float half = _river.Length / 2f - margin;
        foreach (FishingSpot spot in _riverFishingSpots)
        {
            float worldX = _river.Position.X + Dice.FloatRange(-half, half);
            spot.Position = new Vector2(worldX, _river.GetCenterlineWorldY(worldX));
            spot.Refill(3);
        }
        Log("[world] the fish have moved on — new spots have turned up along the river.", "9fc98a");
    }

    // "If the population of bears and wolves drops below threshold,
    // game should auto spawn them. Same with rabbits." One shared
    // floor/ceiling per species — the floor is what this timer
    // enforces; the ceiling also caps Rabbit's own natural multiplying
    // (see TryMultiplyRabbit) so "no overpopulation" holds regardless
    // of which path (timer top-up or multiplying) would otherwise push
    // a count over it.
    private const int MinRabbits = 4, MaxRabbits = 12;
    private const int MinWolves = 2, MaxWolves = 5;
    private const int MinBears = 1, MaxBears = 3;

    private void SpawnInitialAnimals()
    {
        for (int i = 0; i < MinRabbits; i++) SpawnAnimal<Rabbit>(RandomAnimalSpawnPosition());
        for (int i = 0; i < MinWolves; i++) SpawnAnimal<Wolf>(RandomAnimalSpawnPosition());
        for (int i = 0; i < MinBears; i++) SpawnAnimal<Bear>(RandomAnimalSpawnPosition());
    }

    private void BuildAnimalPopulationTimer()
    {
        var timer = new Timer { WaitTime = 30.0, Autostart = true };
        _worldLayer.AddChild(timer);
        timer.Timeout += () =>
        {
            EnsureMinPopulation<Rabbit>(MinRabbits);
            EnsureMinPopulation<Wolf>(MinWolves);
            EnsureMinPopulation<Bear>(MinBears);
        };
    }

    private void EnsureMinPopulation<T>(int min) where T : Animal, new()
    {
        int count = 0;
        foreach (Animal a in _animals) if (a is T) count++;
        for (; count < min; count++)
            SpawnAnimal<T>(RandomAnimalSpawnPosition());
    }

    // Rabbit's own "eat, and multiply within reason" — separate from
    // EnsureMinPopulation above (that's the floor; this is what
    // actually grows the population day to day), but respects the
    // exact same MaxRabbits ceiling, so "no overpopulation" holds no
    // matter which path added the last one.
    private void TryMultiplyRabbit(Rabbit parent)
    {
        int count = 0;
        foreach (Animal a in _animals) if (a is Rabbit) count++;
        if (count >= MaxRabbits) return;
        Vector2 pos = parent.GlobalPosition + new Vector2(Dice.FloatRange(-40f, 40f), Dice.FloatRange(-40f, 40f));
        SpawnAnimal<Rabbit>(pos);
    }

    private Vector2 RandomAnimalSpawnPosition()
    {
        float x = StartingVillageArea.Position.X + Dice.FloatRange(0f, StartingVillageArea.Size.X);
        float y = StartingVillageArea.Position.Y + Dice.FloatRange(0f, StartingVillageArea.Size.Y);
        return new Vector2(x, y);
    }

    // Monotonically increasing, never reused — unlike trees/bushes
    // (which only ever grow, so list position is a stable id), animals
    // die and get removed from the middle of _animals all the time, so
    // "index into the list" can't be the id scheme here.
    private int _nextAnimalId = 0;

    private T SpawnAnimal<T>(Vector2 pos) where T : Animal, new()
    {
        var animal = new T();
        string id = $"animal_{_nextAnimalId++}";
        _worldLayer.AddChild(animal); // Y-sorted, same as any other object with real visual height
        animal.Initialize(_world, pos, Log, _thoughtLog);
        _animals.Add(animal);
        _world.Animals.Add(animal);
        World().Register(id, animal);
        animal.WorldId = id;
        _world.ContentVersion++; // AnimalIds() is cached off this, same as trees/bushes
        animal.Died += () =>
        {
            _animals.Remove(animal);
            _world.Animals.Remove(animal);
            World().Unregister(id);
            _world.ContentVersion++;
        };
        if (animal is Rabbit rabbit)
            rabbit.WantsToMultiply += () => TryMultiplyRabbit(rabbit);
        return animal;
    }

    // The actual footprint of everything hand-placed in BuildWorld()
    // above (village + frontier: mountains/forest/foothills/river-
    // crossing sit roughly x 350-1000, y -530..-340) with real margin
    // beyond both — this is what GroundArea used to be before it became
    // WorldExploration.MaxMapBounds (see GroundArea's own comment).
    // Exists purely to seed WorldExploration.MarkExplored() at boot;
    // nothing else needs "just the curated starting area" as its own rect.
    private static readonly Rect2 StartingVillageArea = new(-400, -600, 2000, 1500);

    // How far ahead of a character content needs to generate to stay
    // offscreen until it's actually approached — derived from the same
    // numbers AttachFollowCamera() sets up, not guessed: at FollowZoom
    // the camera shows roughly (viewport / zoom) world-units, so half
    // the viewport's diagonal is the farthest the player could possibly
    // already be looking. viewport is 1150x880 (project.godot) at zoom
    // 1.6 -> half-width 359, half-height 275, diagonal half ~452. Comes
    // out well under this on purpose — "expand" stretch (project.godot)
    // can show MORE than the design viewport on a wider/taller window,
    // and there's real travel distance to cover between exploration
    // ticks too (see BuildExplorationTimer's WaitTime) — so this is
    // that ~452 plus a generous margin for both, not the bare number.
    private const float LookaheadRadius = 1000f;

    // Checks, on a slow tick rather than every physics frame (region-
    // scale exploration doesn't need per-frame resolution), whether any
    // character — NPC or player — has gotten within LookaheadRadius of
    // a region nobody's been near before, and generates a little more
    // world there if so — well before it's actually reachable on
    // screen, not the moment someone steps into it (see
    // WorldExploration.DiscoverAhead for why that distinction matters).
    // A child of _worldLayer specifically so it inherits that layer's
    // Pausable override and stops ticking along with everything else
    // while the game is paused, rather than Main's own Always mode.
    private void BuildExplorationTimer()
    {
        var timer = new Timer { WaitTime = 2.0, Autostart = true };
        _worldLayer.AddChild(timer);
        timer.Timeout += CheckExploration;
    }

    private void CheckExploration()
    {
        // ToArray(): GenerateContentAt() can append to _agents indirectly
        // in principle (it doesn't today, but iterating a defensive copy
        // costs nothing here and avoids a foreach-over-a-list-you're-
        // mutating bug being possible at all as this grows later).
        foreach (IWorldCharacter agent in _agents.ToArray())
            foreach (Vector2 newRegionCenter in WorldExploration.DiscoverAhead(agent.GlobalPosition, LookaheadRadius))
                GenerateContentAt(newRegionCenter);
    }

    // What actually appears in a newly-discovered region — reuses the
    // exact same classes the hand-placed village already uses (AppleTree,
    // FishingSpot, Foothills, Lake) rather than inventing new
    // procedural art/shapes, so generated content looks and behaves
    // identically to curated content. Most regions stay empty (roll >
    // RiverChance below) — the original village isn't densely packed
    // either, and empty space between landmarks is what makes exploring
    // toward one feel like going somewhere.
    private void GenerateContentAt(Vector2 regionCenter)
    {
        // Small jitter so generated content doesn't land exactly on a
        // grid of region centers — reads as organic instead of a lattice.
        float jitterRange = WorldExploration.RegionSize * 0.3f;
        Vector2 pos = regionCenter + new Vector2(Dice.FloatRange(-jitterRange, jitterRange), Dice.FloatRange(-jitterRange, jitterRange));

        // Grass (56-70, 15%) is deliberately the single most likely
        // non-empty roll here bar trees — common animal food, same
        // "why are rabbits starving" fix as BuildWorld's own initial
        // GrassPatches batch, just keeping density up as the map grows
        // through exploration too, not just at boot.
        int roll = Dice.Roll(100); // 1-100
        if (roll <= 14) GenerateTree(pos);
        else if (roll <= 21) GeneratePine(pos);
        else if (roll <= 28) GenerateOak(pos);
        else if (roll <= 32) GenerateBareTree(pos);
        else if (roll <= 41) GenerateFishingSpot(pos);
        else if (roll <= 55) GenerateBerryBush(pos);
        else if (roll <= 70) GenerateGrassPatch(pos);
        else if (roll <= 74) GeneratePlainBush(pos);
        else if (roll <= 87) GenerateMountainPatch(pos);
        else if (roll <= 98) GenerateLake(pos);

        // A generated tree/fishing spot is a new obstacle and a new
        // resource id every NpcAgent needs to see — cheap enough (a
        // ~1500-cell grid, only on the rare tick something actually
        // generated) to just rebuild in full rather than patching
        // PathGrid incrementally.
        BuildPathGrid();
    }

    private void GenerateTree(Vector2 pos)
    {
        int i = _trees.Count;
        var tree = new AppleTree { Name = $"Tree{i}", Position = NudgeToClearSpot(pos, AppleTree.TrunkRadius), AppleCount = 3 };
        _worldLayer.AddChild(tree);
        World().Register($"tree_{i}", tree);
        _trees.Add(tree);
        _decor.Add(tree);
        _world.ContentVersion++;
        Log($"[world] a new apple tree has grown near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    private void GenerateFishingSpot(Vector2 pos)
    {
        int i = _fishingSpots.Count;
        // FishingSpot has no real collision (Node2D, not IObstacle —
        // water is walkable) — this nudge is purely so a generated one
        // doesn't visually land inside a tree trunk or bush, not a
        // physics necessity. A generic 20px stand-in radius, since it
        // has no TrunkRadius of its own to read.
        var spot = new FishingSpot { Name = $"FishingSpot{i}", Position = NudgeToClearSpot(pos, 20f), FishCount = 3 };
        _worldLayer.AddChild(spot);
        World().Register($"fish_{i}", spot);
        _fishingSpots.Add(spot);
        _decor.Add(spot);
        _world.ContentVersion++;
        Log($"[world] a new fishing spot has been found near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    private void GeneratePine(Vector2 pos)
    {
        int i = _pineTrees.Count;
        const float trunkRadius = 12f;
        var pine = new GatherableFoliage
        {
            Name = $"Pine{i}", Position = NudgeToClearSpot(pos, trunkRadius), Count = 3,
            ActionId = "gather_pinecone", ItemName = "pinecone",
            TexturePath = "res://assets/world/pine.png", SpriteScale = 4.5f, TrunkRadius = trunkRadius,
        };
        _worldLayer.AddChild(pine);
        World().Register($"pine_{i}", pine);
        _pineTrees.Add(pine);
        _decor.Add(pine);
        _world.ContentVersion++;
        Log($"[world] a pine tree has taken root near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    private static readonly (string Item, Color Tint)[] BerryKinds =
    {
        ("blueberry", new Color(0.5f, 0.9f, 2.0f)),
        ("blackberry", new Color(0.55f, 0.75f, 1.5f)),
        ("raspberry", new Color(1.2f, 0.6f, 0.6f)),
    };

    private void GenerateBerryBush(Vector2 pos)
    {
        (string item, Color tint) = BerryKinds[Dice.Roll(BerryKinds.Length) - 1];
        int i = _berryBushes.Count;
        const float trunkRadius = 8f;
        var bush = new GatherableFoliage
        {
            Name = $"Berry{i}", Position = NudgeToClearSpot(pos, trunkRadius), Count = 3,
            ActionId = "gather_berry", ItemName = item,
            TexturePath = "res://assets/world/bush_berry.png", SpriteScale = 2.5f, TrunkRadius = trunkRadius,
            Tint = tint,
            RegenSeconds = 60f, // same regrowth as the hand-placed starting bushes
        };
        _worldLayer.AddChild(bush);
        World().Register($"berry_{i}", bush);
        _berryBushes.Add(bush);
        _decor.Add(bush);
        _world.ContentVersion++;
        Log($"[world] a {item} bush has grown near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    // Animal food only — see WorldContext.GrassPatches's own header.
    // Not version-bumped (_world.ContentVersion++) like the other
    // Generate* calls above — that counter exists purely to invalidate
    // NpcAgent's cached TreeIds()/FishingSpotIds() target-id lists, and
    // grass patches were deliberately never given a human-facing
    // target-id enum to begin with (ActionId "" — no gather_* action
    // ever lists them), so there is nothing here for that cache to go
    // stale about.
    private void GenerateGrassPatch(Vector2 pos)
    {
        SpawnGrassPatch(pos);
        Log($"[world] a patch of grass has grown near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    // Shared by both the hand-placed starting batch (BuildWorld, above)
    // and GenerateGrassPatch — identical GatherableFoliage construction
    // either way, extracted so a future tweak (new field, different
    // texture, adjusted scale) can't land in one call site and get
    // silently forgotten in the other, which is exactly what had
    // already started happening (each site had its own, separately
    // reworded copy of the same TrunkRadius comment).
    private GatherableFoliage SpawnGrassPatch(Vector2 pos)
    {
        int i = _grassPatches.Count;
        const float trunkRadius = 6f; // spacing only now (Walkable skips the actual collision/obstacle circle this would otherwise become)
        // A real finite Count again, not the Count=9999 "basically
        // unlimited" patch this went through first (see git history) —
        // that fixed the actual reported bug (every rabbit AND bear on
        // the map was drawing from the same non-renewing Count=2 pool,
        // 8 patches × 2 bites = a hard cap of 16 meals for the entire
        // wildlife population, ever) but by papering over depletion
        // entirely rather than fixing it. RegenSeconds now does that
        // properly: grazed down to 0 like any real patch, back to full
        // ~1 minute after the last bite (see GatherableFoliage's own
        // header for why that's the more honest fix).
        var patch = new GatherableFoliage
        {
            Name = $"Grass{i}", Position = NudgeToClearSpot(pos, trunkRadius), Count = 3,
            ActionId = "", ItemName = "grass",
            TexturePath = "res://assets/world/grass_patch.png", SpriteScale = 0.65f, TrunkRadius = trunkRadius,
            Walkable = true, // ground cover for rabbits/herbivores to graze — every character should walk straight over it, not detour around it
            RegenSeconds = 60f,
        };
        // AddBackgroundNode, not _worldLayer.AddChild — _worldLayer is
        // YSortEnabled (see its own field comment), which is exactly
        // right for a bush or tree a character can stand in front of OR
        // behind, but wrong for flat ground cover with no real height:
        // GatherableFoliage's shared bottom-anchored sprite offset made
        // Y-sort compare the grass's OWN anchor point against a
        // character's feet each frame, so standing even slightly
        // "below" a patch drew the grass sprite on top of the character
        // instead of under them. Same "always behind, never Y-sorted"
        // treatment River/Lake/Foothills/ForestPatch already get, for
        // the same reason.
        AddBackgroundNode(patch);
        World().Register($"grass_{i}", patch);
        _grassPatches.Add(patch);
        _decor.Add(patch);
        return patch;
    }

    // Standalone foliage — oak, the bare/dead tree, a plain bush —
    // never registered (nothing ever looks one up by id, same as
    // Foothills/ForestPatch aren't either) and never bumps
    // ContentVersion (they're not in any NpcAgent-cached target list).
    // Y-sorted in _worldLayer like every other object with real visual
    // height, unlike the flat backdrop pieces below.
    private void GenerateOak(Vector2 pos)
    {
        var oak = new DecorativeFoliage { Name = $"Oak{_decor.Count}", Position = NudgeToClearSpot(pos, 12f), TexturePath = "res://assets/world/oak.png", SpriteScale = 4.5f, TrunkRadius = 12f };
        _worldLayer.AddChild(oak);
        _decor.Add(oak);
        Log($"[world] an oak tree has grown near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    private void GenerateBareTree(Vector2 pos)
    {
        var tree = new DecorativeFoliage { Name = $"BareTree{_decor.Count}", Position = NudgeToClearSpot(pos, 10f), TexturePath = "res://assets/world/bare_tree.png", SpriteScale = 4.5f, TrunkRadius = 10f };
        _worldLayer.AddChild(tree);
        _decor.Add(tree);
        Log($"[world] a bare, leafless tree stands near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    private void GeneratePlainBush(Vector2 pos)
    {
        var bush = new DecorativeFoliage { Name = $"PlainBush{_decor.Count}", Position = NudgeToClearSpot(pos, 8f), TexturePath = "res://assets/world/bush_plain.png", SpriteScale = 2.5f, TrunkRadius = 8f };
        _worldLayer.AddChild(bush);
        _decor.Add(bush);
        Log($"[world] a bush has grown near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    // Decorative and solid, like the hand-placed Foothills — but NOT
    // registered as a flagpole/travel destination. Keeping generated
    // mountains purely backdrop avoids the travel tool's target list
    // growing without bound right alongside the map; misty_mountains
    // stays the one named "far-off landmark" NPCs can reason about.
    private void GenerateMountainPatch(Vector2 pos)
    {
        const float radius = 70f; // Foothills' own default Radius
        var patch = new Foothills { Name = $"GeneratedFoothills{_decor.Count}", Position = NudgeToClearSpot(pos, radius) };
        AddBackgroundNode(patch);
        _decor.Add(patch);
        Log($"[world] new foothills have come into view near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    // A round lake (see Lake's own header), not the village's wavy
    // River ribbon — replicating THAT procedurally would risk landing
    // badly without being able to render and check it, the same
    // reasoning that kept this a localized water patch rather than a
    // second full river before. Decorative and solid, same "always
    // behind, never Y-sorted" terrain treatment as every other
    // background piece. A smaller default Radius than the hand-placed
    // mountain lake — these turn up scattered all over an exploring
    // player's path, not just once as a single landmark.
    private void GenerateLake(Vector2 pos)
    {
        const float radius = 90f;
        // NudgeToClearSpot gets the actual obstacle radius (0.9x,
        // matching Lake.GetObstacleCircles()), not the full visual
        // Radius — passing the bigger number would only over-space
        // generated lakes from their neighbors versus what the real
        // collision circle needs.
        var patch = new Lake { Name = $"GeneratedLake{_decor.Count}", Position = NudgeToClearSpot(pos, radius * 0.9f), Radius = radius };
        AddBackgroundNode(patch);
        _decor.Add(patch);
        Log($"[world] a small lake glimmers near ({pos.X:0}, {pos.Y:0}).", "9fc98a");
    }

    // Adds a Main-level child and forces it BEFORE _worldLayer in
    // sibling order, regardless of when it's added — the same "always
    // behind" placement BuildWorld() gets for free by construction
    // (added before _worldLayer exists at all), but generated content
    // arrives long after _worldLayer already exists, so plain AddChild()
    // would make it a LATER sibling and render it ON TOP of everything
    // Y-sorted — the exact bug fixed once already for the hand-placed
    // river/mountains (see _worldLayer's own comment).
    private void AddBackgroundNode(Node2D node)
    {
        AddChild(node);
        MoveChild(node, _worldLayer.GetIndex());
    }

    // The starting village-core footprint — used as a FLOOR by
    // ComputePathGridArea() below, not the actual grid area anymore.
    // This used to BE the whole grid, on the reasoning that "travel"
    // (the one thing that goes all the way to the frontier) never
    // reads PathGrid anyway. That missed gather-type targets
    // (pick_up_stick, gather_berry, ...) generated by exploration far
    // outside this rect: GenerateContentAt scatters those across the
    // full WorldExploration.MaxMapBounds, but PathGrid.FindPath's own
    // WorldToCell CLAMPS an out-of-bounds target to the grid's nearest
    // edge cell rather than rejecting it — so a real, walkable berry
    // bush a few thousand px into the frontier routed toward the wrong
    // point entirely, then finished the trip with zero obstacle
    // avoidance past the grid edge. Combined with NpcAgent's own
    // SortByDistance (gather targets now always resolve to the nearest
    // REAL instance, however far that is once nearby ones are used up)
    // this was a real, reported "gather_berry -> unreachable" failure,
    // not a hypothetical one — a genuinely walkable target reported as
    // unreachable because nothing checked whether it was even IN the
    // grid this path was being planned on.
    private static readonly Rect2 PathGridArea = new(-50, -50, 1250, 850);

    // Grows PathGridArea to actually cover wherever content has been
    // generated, instead of either the tiny fixed village rect (which
    // clamped far-off gather targets to the wrong cell — see
    // PathGridArea's own header) or the full MaxMapBounds up front
    // (which would size the grid for the theoretical max playable area
    // from turn one, even on a fresh session where almost none of it
    // is actually populated yet — BuildPathGrid's own cost scales with
    // cell count × obstacle count, so that's real, avoidable work for
    // empty space). Recomputed fresh each BuildPathGrid() call — cheap,
    // one pass over _decor plus _sticks (the one gatherable list that
    // isn't also in _decor) — so the grid only ever grows to match
    // actual exploration, the same "no bigger than it needs to be
    // right now" shape TooCloseToExisting's own ring search already
    // has. Clamped to MaxMapBounds as a ceiling: nothing ever
    // legitimately generates outside it, so the grid never needs to
    // either.
    private Rect2 ComputePathGridArea()
    {
        float minX = PathGridArea.Position.X, minY = PathGridArea.Position.Y;
        float maxX = PathGridArea.End.X, maxY = PathGridArea.End.Y;

        void Expand(Vector2 pos)
        {
            minX = Mathf.Min(minX, pos.X);
            minY = Mathf.Min(minY, pos.Y);
            maxX = Mathf.Max(maxX, pos.X);
            maxY = Mathf.Max(maxY, pos.Y);
        }

        foreach (Node2D node in _decor)
            Expand(node.Position);
        foreach (Stick stick in _sticks)
            Expand(stick.Position);

        const float Padding = 200f; // real margin around the outermost object, not a rect that ends exactly on top of it
        Rect2 bounds = WorldExploration.MaxMapBounds;
        minX = Mathf.Max(minX - Padding, bounds.Position.X);
        minY = Mathf.Max(minY - Padding, bounds.Position.Y);
        maxX = Mathf.Min(maxX + Padding, bounds.End.X);
        maxY = Mathf.Min(maxY + Padding, bounds.End.Y);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    // Deliberately NOT the same rect as PathGridArea — this first pass
    // covered only the resource area (matching PathGridArea's own
    // deliberate village-only scope), and with the camera now following
    // the player around instead of holding a fixed full-map framing, the
    // player can easily walk to where that rect's edge was and see the
    // raw background clear color start abruptly. PathGridArea itself is
    // untouched; widening it too would grow the A* grid for no reason
    // since "travel" toward the frontier never uses it anyway.
    //
    // Same WorldExploration.MaxMapBounds rect that bounds exploration-
    // driven generation, not an independent guess — grass is a single
    // cheap repeating Sprite2D regardless of how big a rect it covers
    // (RegionRect + TextureRepeat tiles at render time, one draw call),
    // so there's no reason to grow it incrementally as new regions get
    // discovered the way trees/mountains/rivers do. Painting the whole
    // max playable area up front means the ground can never fall behind
    // exploration and re-expose the old "grass stops abruptly" bug —
    // and tying it to the SAME constant WorldExploration uses means the
    // two can't drift out of sync if that bound ever changes.
    private static readonly Rect2 GroundArea = WorldExploration.MaxMapBounds;

    // One tiled grass Sprite2D covering GroundArea — a single node, not
    // a grid of hundreds of small ones: Sprite2D's RegionRect, given a
    // region LARGER than the source texture plus TextureRepeat.Enabled,
    // samples the 16×16 tile repeatedly to fill it, and Scale then blows
    // the whole thing up uniformly so each repeat reads at
    // GroundTileWorldSize on screen instead of the source's native 16px.
    // Added first, before anything else in BuildWorld(), so every other
    // object's default sibling draw order puts it on top — this is the
    // only thing in the scene that actually needs to be "the floor."
    private const float GroundTileWorldSize = 32f;

    private void BuildGround()
    {
        float s = GroundTileWorldSize / 16f;
        var ground = new Sprite2D
        {
            Name = "Ground",
            Texture = GD.Load<Texture2D>("res://assets/world/grass.png"),
            Centered = false,
            Position = GroundArea.Position,
            RegionEnabled = true,
            RegionRect = new Rect2(0, 0, GroundArea.Size.X / s, GroundArea.Size.Y / s),
            Scale = new Vector2(s, s),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
        };
        AddChild(ground);
    }

    // Dims the ENTIRE 2D world uniformly by multiplying every pixel's
    // color by DimColor — CanvasModulate only affects whatever canvas it
    // shares, which is the main 2D scene (Home, trees, characters,
    // ground) and NOT the UI (that's its own separate CanvasLayer,
    // untouched by this on purpose — the log panel and action buttons
    // shouldn't dim along with the world). This darkens everything to a
    // flat baseline; it does NOT make unlit areas black or hide them —
    // Godot's 2D lights (PointLight2D, see PlayerCharacter's
    // BuildVisibilityLight()) work ADDITIVELY on top of this, brightening
    // their covered radius back up rather than the other way around
    // (there's no "invisible until lit" occlusion here — that's the
    // actual fog-of-war step, deliberately not this one).
    //
    // The "night-time later" this comment used to promise is
    // DayNightCycle now — a CanvasModulate subclass that cycles this
    // same baseline between day and a darker, blue-purple night rather
    // than sitting at one fixed color forever. A child of _worldLayer
    // (Pausable), not Main (Always), so its own clock freezes right
    // along with everything else while the game is paused — see its
    // own header for the full reasoning.
    private void BuildLighting()
    {
        _worldLayer.AddChild(new DayNightCycle { Name = "WorldDimmer" });
    }

    // "Check if anything existing is already there before placing
    // anything — same goes for all objects." A grass patch got placed
    // directly on top of the fire pit; a hand-picked coordinate that
    // LOOKS clear on paper is exactly how that happened. This is the
    // real runtime check instead: every object already in _decor that
    // implements IObstacle, at its own actual GetObstacleCircles()
    // footprint — the SAME data BuildPathGrid() below builds the whole
    // NPC pathing obstacle list from, not a separate, possibly-
    // inconsistent notion of "occupied." newObjectRadius is the thing
    // about to be placed's own footprint, so two small objects can sit
    // closer together than two large ones — this isn't just "is the
    // CENTER point free."
    private const float PlacementMinGap = 12f; // a little visual breathing room beyond bare non-overlap

    private bool TooCloseToExisting(Vector2 pos, float newObjectRadius)
    {
        foreach (Node2D deco in _decor)
        {
            // Walkable GatherableFoliage (grass) deliberately yields no
            // circles from GetObstacleCircles() — IObstacle's own
            // contract is "physically blocks movement," and walkable
            // ground cover doesn't. But that means it's otherwise
            // invisible to THIS check too, since both read the same
            // method — two grass patches could land exactly on top of
            // each other with nothing to stop it. Checked against its
            // own TrunkRadius directly (the same number it would have
            // contributed to GetObstacleCircles() if it weren't
            // walkable) rather than changing what IObstacle means for
            // every other implementer just to fix spacing for one.
            if (deco is GatherableFoliage { Walkable: true } walkable)
            {
                if (pos.DistanceTo(deco.Position) < walkable.TrunkRadius + newObjectRadius + PlacementMinGap)
                    return true;
                continue;
            }

            if (deco is not IObstacle obstacle) continue;
            foreach ((Vector2 offset, float radius) in obstacle.GetObstacleCircles())
                if (pos.DistanceTo(deco.Position + offset) < radius + newObjectRadius + PlacementMinGap)
                    return true;
        }

        // "We also got trees growing in the river." River is
        // deliberately NOT an IObstacle (shallow, walkable, no
        // collision by design — see its own header) so the _decor loop
        // above never sees it at all. That's correct for movement, but
        // wrong for PLACEMENT — nothing should visually spawn standing
        // in the water regardless of whether it can walk there. Checked
        // against the river's own true (wavy) centerline at this exact
        // x, same as fishing spots already place themselves precisely
        // on it via GetCenterlineWorldY — not a straight-line guess that
        // would be wrong at some x along the sine curve.
        if (_river != null)
        {
            float halfSpan = _river.Length / 2f;
            float localX = pos.X - _river.Position.X;
            if (localX >= -halfSpan && localX <= halfSpan)
            {
                float centerY = _river.GetCenterlineWorldY(pos.X);
                if (Mathf.Abs(pos.Y - centerY) < _river.Thickness / 2f + newObjectRadius + PlacementMinGap)
                    return true;
            }
        }

        return false;
    }

    // Nudges a candidate position to the nearest actually-clear spot if
    // something's already there — a small outward ring search around
    // the original intent (not a random relocation), so a curated
    // hand-placed position stays close to where it was meant to go, and
    // a procedurally-jittered one stays within its own region. Used by
    // every Generate*/hand-placed batch below. Gives up and returns the
    // original position if 8 rings out don't find anything clear —
    // placed imperfectly is still better than not placed at all, and by
    // that point something has gone stranger than an ordinary crowded
    // corner of the map.
    private Vector2 NudgeToClearSpot(Vector2 desired, float newObjectRadius)
    {
        if (!TooCloseToExisting(desired, newObjectRadius))
            return desired;
        // 8 rings × 24px (192px max reach) used to be enough for the
        // usual case — one nearby object, a small nudge clears it. The
        // hand-placed mountain lake needed real distance from TWO large
        // neighbors at once (Foothills' full circle and Misty
        // Mountains' own flank circles, both over 100px radius) — every
        // candidate within 192px of its hand-picked spot was still too
        // close to one or the other, so this silently gave up and
        // returned the original, still-overlapping position instead of
        // ever finding the genuinely clear spot that exists further
        // out. 20 rings (480px max reach) gives a real placement enough
        // room to escape a cluster like that.
        for (int ring = 1; ring <= 20; ring++)
        {
            float ringRadius = ring * 24f;
            const int samples = 8;
            for (int s = 0; s < samples; s++)
            {
                float angle = Mathf.Tau * s / samples;
                Vector2 candidate = desired + ringRadius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (!TooCloseToExisting(candidate, newObjectRadius))
                    return candidate;
            }
        }
        return desired;
    }

    private void BuildPathGrid()
    {
        var obstacles = new List<(Vector2 Center, float Radius)>();
        foreach (Node2D node in _decor)
            if (node is IObstacle obstacle)
                foreach ((Vector2 offset, float radius) in obstacle.GetObstacleCircles())
                    obstacles.Add((node.Position + offset, radius));

        PathGrid.Build(ComputePathGridArea(), obstacles);
    }

    private WorldRegistry World() => GetNode<WorldRegistry>("/root/World");

    // Space, not an Input Map action — same reasoning as the player's
    // WASD/number-key handling (see PlayerCharacter): avoids hand-
    // editing project.godot's [input] section. Used to be "P"; moved to
    // Space once Space stopped meaning "activate the topmost action"
    // there (number keys 1-9 cover that now, more precisely — which
    // action, not just whichever's on top) — freed up the single most
    // reachable key on the board for the single most reached-for
    // shortcut. Lives on Main, not PlayerCharacter, deliberately —
    // PlayerCharacter sits under _worldLayer, which is exactly what
    // needs to STOP receiving input while paused; putting the toggle
    // there would mean Space could pause the game but never unpause it.
    // Main.ProcessMode is Always (see _Ready()) specifically so this
    // keeps firing regardless of pause state, in both directions.
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
        {
            TogglePause();
            GetViewport().SetInputAsHandled();
        }
    }

    private void TogglePause()
    {
        bool paused = !GetTree().Paused;
        GetTree().Paused = paused;
        GetNode<Button>("UI/PauseButton").Text = paused ? "Resume" : "Pause";
    }

    // Wires the Settings button/panel (see Main.tscn's own comment on
    // that node — structure lives there, visuals and behavior live
    // here). Deliberately does NOT auto-pause when opened — everything
    // in the panel is a quick, deliberate click (toggle a checkbox,
    // restart, exit), not something that needs the world frozen to use
    // safely; Space is still right there if the player wants that.
    private void BuildSettingsMenu()
    {
        var settingsButton = GetNode<Button>("UI/SettingsButton");
        var panel = GetNode<PanelContainer>("UI/SettingsPanel");
        var vitalsBarsCheck = GetNode<CheckBox>("UI/SettingsPanel/Margin/VBox/VitalsBarsCheck");
        var restartButton = GetNode<Button>("UI/SettingsPanel/Margin/VBox/RestartButton");
        var exitButton = GetNode<Button>("UI/SettingsPanel/Margin/VBox/ExitButton");
        var closeButton = GetNode<Button>("UI/SettingsPanel/Margin/VBox/CloseButton");

        // A real background, not the barely-visible default PanelContainer
        // style — same dark-panel-on-garden aesthetic as LogPanel's own
        // ColorRect, just as a StyleBox here since PanelContainer draws
        // through one rather than being one.
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.07f, 0.05f, 0.97f),
            BorderColor = new Color(0.4f, 0.5f, 0.35f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        });
        foreach (Label label in panel.FindChildren("*", "Label"))
            label.AddThemeColorOverride("font_color", new Color(0.92f, 0.97f, 0.9f));
        foreach (CheckBox check in panel.FindChildren("*", "CheckBox"))
            check.AddThemeColorOverride("font_color", new Color(0.92f, 0.97f, 0.9f));

        vitalsBarsCheck.ButtonPressed = GameSettings.ShowVitalsBars;

        settingsButton.Pressed += () => panel.Visible = !panel.Visible;
        closeButton.Pressed += () => panel.Visible = false;
        vitalsBarsCheck.Toggled += pressed => GameSettings.ShowVitalsBars = pressed;

        // Unpause explicitly first — SceneTree.Paused is a tree-level
        // flag ReloadCurrentScene() doesn't reset on its own, so
        // restarting while paused would otherwise hand back a brand
        // new game that's frozen from the very first frame with no
        // obvious way back (the new PauseButton reads "Pause", not
        // "Resume," since its own _Ready() has no idea the tree is
        // still paused).
        restartButton.Pressed += () =>
        {
            GetTree().Paused = false;
            _thoughtLog?.Close();
            GetTree().ReloadCurrentScene();
        };
        exitButton.Pressed += () => { _thoughtLog?.Close(); GetTree().Quit(); };
    }

    // Flushes/closes the NPC thought log's own kept-open file handle
    // (see NpcThoughtLogger's own header for why it's kept open rather
    // than reopened per line) on the OS window's close button too, not
    // just the in-UI Exit button above — doesn't touch
    // SceneTree.AutoAcceptQuit, so the tree still quits exactly the way
    // it always did; this just also runs cleanup alongside that.
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            _thoughtLog?.Close();
    }

    // AppendText(), not a full Text replace of a rebuilt _logLines join —
    // DebugLog's scroll_follow=true (Main.tscn) is Godot's own "only
    // autoscroll if the user was already at the bottom, leave it alone
    // if they scrolled up to read" behavior, but it's built around
    // incremental content growth. Replacing the ENTIRE Text every single
    // line defeated it — from RichTextLabel's perspective that's a new
    // block of content each time, not "content added to what's already
    // there," so it had no reliable "was I at the bottom" state to act
    // on. RemoveParagraph(0) trims old lines the same way _logLines used
    // to (each Log() call is one paragraph), without ever touching Text
    // as a whole.
    // Was 40 — far too short once filtering could hide a chunk of the
    // stream for a while ("I hope once the filter is removed I can
    // read missed events... it should just be a UI filter, not
    // actually throw away output"): 40 lines shared across the world
    // plus every NPC's own thinking/speaking/acting was only a couple
    // turns' worth, so anything before that was already gone by the
    // time you'd want to look back at it, filter or no filter. This is
    // just what the visible RichTextLabel itself keeps as paragraphs
    // (a real render/scroll cost, so still bounded) — MaxLogHistoryLines
    // below is the much bigger cap on what's actually remembered.
    private const int MaxVisibleLogLines = 300;

    // The real backing memory for everything logged, filtered out of
    // the current view or not — see _logHistory's own header. Plain
    // (Speaker, Text, Color) structs, not rendered anywhere until
    // RebuildDebugLogDisplay walks them, so this can afford to hold a
    // lot more than MaxVisibleLogLines without costing anything until
    // someone actually toggles a filter or scrolls back.
    private const int MaxLogHistoryLines = 2000;

    // Explicit, not scroll_follow (Main.tscn had that on originally,
    // now off) — scroll_follow's own "was I already at the bottom"
    // heuristic is built around plain content GROWTH, and every Log()
    // call here does an AppendText immediately followed by a
    // RemoveParagraph(0) trim in the same tick once the log's past
    // MaxLogLines. That compound add-then-trim is what actually caused
    // "sometimes auto-scrolls, sometimes doesn't" — verified headlessly
    // that scroll_follow's own bottom-tracking silently fails to keep
    // up across that pattern. Doing the check and the scroll ourselves
    // removes the guesswork: read the scrollbar BEFORE touching
    // content, then explicitly scroll to the true bottom AFTER, in the
    // same call — verified headlessly this needs no extra frame to
    // "settle" first (an earlier version deferred this a frame on the
    // theory that Godot's layout wouldn't have caught up yet by the
    // time MaxValue/GetLineCount() were read; that theory turned out
    // wrong AND the deferral introduced its own real bug: a
    // CallDeferred scheduled during Main's own _Ready() — i.e. every
    // boot-time spawn line — never actually fired, leaving the log
    // sitting unscrolled at the top until something else nudged it
    // later. Calling ScrollToLine() immediately, synchronously, fixed
    // both).
    // The exact (line, color) last written — not just line text, since
    // the same words in a different color would read as a genuinely
    // different event. Compared BEFORE the 200-char truncation/bbcode
    // escaping below so two calls that only differ past the cutoff
    // still count as identical (matches what the reader would actually
    // see either way).
    private string _lastLoggedLine;
    private string _lastLoggedColor;

    // Everything ever logged, bounded to MaxLogHistoryLines — kept
    // independently of what's currently on screen (and of
    // MaxVisibleLogLines, the much smaller cap on the RichTextLabel's
    // own paragraphs) so a filter toggle can rebuild the visible view
    // from real history, not just start filtering whatever gets logged
    // from that point on: this is what makes filtering purely a VIEW
    // over the log, never an actual loss of anything logged while it
    // was active — clear the filter and everything that happened while
    // it was on is still right there. See SetActiveLogFilter/
    // RebuildDebugLogDisplay.
    private struct LogLine
    {
        public string Speaker;
        public string Text;
        public string Color;
    }
    private readonly List<LogLine> _logHistory = new();

    // null = no filter, every line shows. Otherwise this is exactly
    // one DisplayName (an NpcAgent's own — see ExtractSpeaker) and only
    // lines starting with that bracket are visible. Set/cleared by
    // SetActiveLogFilter, from either a filter-bar button click or
    // RefreshNpcFilterBar noticing its NPC walked out of range.
    private string _activeLogFilter;

    private void Log(string line, string color = "d8ddd0")
    {
        // "If prev row is exact same, don't log it" — a per-tick
        // decision loop (an animal re-deciding "still fleeing the same
        // wolf" every physics frame, say) can otherwise call this with
        // the identical line dozens of times a second even when
        // nothing actually changed, burying everything else under
        // repeats of one event. Only collapses IMMEDIATE repeats
        // (three different lines then the same one again still logs),
        // which is what "spam from one stuck event" actually looks
        // like, not a blanket "never say this twice" rule.
        if (line == _lastLoggedLine && color == _lastLoggedColor)
            return;
        _lastLoggedLine = line;
        _lastLoggedColor = color;

        // Extracted from the RAW line, before bbcode-escaping below
        // turns real brackets into "[lb]" and makes them unparseable.
        // Every logged line starts with "[Whoever]" by convention —
        // NpcAgent's own _uiLog calls, Animal.LogEvent, and this
        // class's own "[world]"/"[player]" lines all follow it.
        string speaker = ExtractSpeaker(line);

        // escape literal brackets so LLM text can never be read as a bbcode tag
        string safe = line.Replace("[", "[lb]");
        if (safe.Length > 200)
            safe = safe.Substring(0, 197) + "...";

        _logHistory.Add(new LogLine { Speaker = speaker, Text = safe, Color = color });
        while (_logHistory.Count > MaxLogHistoryLines)
            _logHistory.RemoveAt(0);

        // Filtered out of the live view right now — still recorded
        // above, so clearing the filter later brings it right back
        // instead of only whatever logs from that point on.
        if (_activeLogFilter != null && speaker != _activeLogFilter)
            return;

        AppendVisibleLine(safe, color);
    }

    private static string ExtractSpeaker(string line)
    {
        if (string.IsNullOrEmpty(line) || line[0] != '[') return null;
        int close = line.IndexOf(']');
        return close > 1 ? line.Substring(1, close - 1) : null;
    }

    private void AppendVisibleLine(string safe, string color)
    {
        VScrollBar scrollBar = _debugLog.GetVScrollBar();
        // page is how much content is already visible — "at the bottom"
        // means the visible page's far edge already reaches MaxValue,
        // not that Value itself equals MaxValue (page > 0 whenever
        // there's more content than fits). The 1px slack absorbs float
        // rounding, same reasoning as any other "close enough" scroll
        // check. No scrollbar yet (nothing's overflowed the box) counts
        // as "at the bottom" too — there's nowhere else it could be.
        bool wasAtBottom = scrollBar == null || scrollBar.Value >= scrollBar.MaxValue - scrollBar.Page - 1.0;

        _debugLog.AppendText($"[color=#{color}]{safe}[/color]\n");
        while (_debugLog.GetParagraphCount() > MaxVisibleLogLines)
            _debugLog.RemoveParagraph(0);

        // ScrollToLine(), not scrollBar.Value = scrollBar.MaxValue —
        // tried that first and verified headlessly that setting Value
        // directly is silently ineffective (reads back as if nothing
        // happened; GetVScrollBar() appears to hand back a read-only
        // reflection of RichTextLabel's own internal scroll state, not
        // something that drives it). ScrollToLine() is RichTextLabel's
        // own real API for this and verified to land exactly at the
        // true bottom (max_value - page, not just approximately).
        if (wasAtBottom)
            _debugLog.ScrollToLine(_debugLog.GetLineCount());
    }

    // Full re-render of the visible log from _logHistory under whatever
    // _activeLogFilter is now set to — the only way a filter toggle can
    // affect lines already on screen, not just new ones from this point
    // forward. Capped to the most recent MaxVisibleLogLines MATCHING
    // entries (not all up to MaxLogHistoryLines) so this renders the
    // same amount of content a live, unfiltered Log() stream would —
    // clearing a filter shows a full screen of Everyone's recent
    // activity again, not a sudden wall of thousands of lines. Always
    // ends scrolled to the bottom of whatever's now showing, same as a
    // live Log() call would.
    private void RebuildDebugLogDisplay()
    {
        var visible = new List<LogLine>(MaxVisibleLogLines);
        for (int i = _logHistory.Count - 1; i >= 0 && visible.Count < MaxVisibleLogLines; i--)
        {
            LogLine entry = _logHistory[i];
            if (_activeLogFilter == null || entry.Speaker == _activeLogFilter)
                visible.Add(entry);
        }
        visible.Reverse();

        _debugLog.Clear();
        foreach (LogLine entry in visible)
            _debugLog.AppendText($"[color=#{entry.Color}]{entry.Text}[/color]\n");
        _debugLog.ScrollToLine(_debugLog.GetLineCount());
    }

    // "Add filter buttons to the top for nearby NPCs... once I'm not in
    // their vicinity anymore, remove the filter automatically." Run on
    // a real-time timer from _Process (see NpcFilterRefreshInterval),
    // not every frame — a handful of NPCs, no need to rescan that often.
    // Diffs the button bar against who's actually within
    // SpeechLog.HearingRadius of the player right now: adds a button
    // for anyone newly in range, removes one for anyone who's left —
    // and if the NPC that just left was the active filter, clears it
    // too, rather than leaving the log stuck filtered on someone with
    // no button left to un-filter it.
    private void RefreshNpcFilterBar()
    {
        if (_player == null) return;

        var nearby = new HashSet<string>();
        foreach (IWorldCharacter agent in _agents)
        {
            if (ReferenceEquals(agent, _player)) continue;
            if (agent.GlobalPosition.DistanceTo(_player.GlobalPosition) <= SpeechLog.HearingRadius)
                nearby.Add(agent.DisplayName);
        }

        foreach (string name in new List<string>(_npcFilterButtons.Keys))
        {
            if (nearby.Contains(name)) continue;
            _npcFilterButtons[name].QueueFree();
            _npcFilterButtons.Remove(name);
            if (_activeLogFilter == name)
                SetActiveLogFilter(null);
        }

        foreach (string name in nearby)
        {
            if (_npcFilterButtons.ContainsKey(name)) continue;
            var button = new Button
            {
                Text = name,
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
            };
            // Captured 'name', not the button's own .Text — same
            // string either way, but this is the one actually compared
            // against elsewhere (dictionary key, _activeLogFilter).
            button.Pressed += () => SetActiveLogFilter(_activeLogFilter == name ? null : name);
            _npcFilterBar.AddChild(button);
            _npcFilterButtons[name] = button;
        }
    }

    // One filter active at a time — clicking the already-active button
    // clears it (back to seeing everything), clicking a different one
    // switches straight to it. SetPressedNoSignal, not .ButtonPressed =,
    // so reflecting the new state on every button doesn't itself fire
    // another Pressed and recurse.
    private void SetActiveLogFilter(string name)
    {
        _activeLogFilter = name;
        foreach (KeyValuePair<string, Button> kv in _npcFilterButtons)
            kv.Value.SetPressedNoSignal(kv.Key == name);
        RebuildDebugLogDisplay();
    }
}
