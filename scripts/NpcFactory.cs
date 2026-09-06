using Godot;

// The one place that knows how to assemble a fully-wired NPC: actor +
// visual, its own Mind (backed by its own provider instance — see the
// note on NpcAgent about why that can't be shared), registration in
// WorldRegistry, and the agent that drives it all. Everything else
// treats "add an NPC" as one call.
public static class NpcFactory
{
    public static NpcAgent Create(
        Node parent,
        WorldRegistry world,
        MindConfig config,
        NpcThoughtLogger thoughtLog,
        System.Action<string, string> uiLog,
        string id,
        Personality personality,
        Vector2 startPosition,
        WorldContext worldContext,
        int spriteVariant)
    {
        var actor = new NPCActor { Name = personality.Name, Position = startPosition };

        var agent = new NpcAgent();
        parent.AddChild(agent); // must be in the tree before Initialize() adds children under it
        agent.Initialize(id, personality, actor, config.CreateProvider(), worldContext, thoughtLog, uiLog, config.PureLlmMode);

        // Only safe after Initialize() — that's what calls AddChild(actor),
        // which is what runs NPCActor._Ready() and actually creates the
        // Sprite node SetCharacterSprite() configures. Calling this any
        // earlier (actor isn't in the scene tree yet) would hit a null
        // sprite.
        actor.SetCharacterSprite(spriteVariant);

        world.Register(id, actor);
        // Also registered under its display name — this is what lets
        // "follow" target an NPC by the same name perception/speech/
        // memory already use, resolved through the exact same
        // WorldRegistry lookup NPCActor uses for every other action,
        // no separate name-to-id translation layer needed.
        world.Register(personality.Name, actor);
        return agent;
    }
}
