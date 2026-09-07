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

        // actor goes directly under parent (the Y-sorted world layer) —
        // the SAME tree depth PlayerCharacter sits at (see
        // Main.CreatePlayer), not nested a level deeper under NpcAgent
        // below. This used to be AddChild(actor) inside
        // NpcAgent.Initialize(), making NpcAgent (a plain Node, not a
        // CanvasItem) actor's real scene-tree parent — Y-sort compares
        // canvas-item depth, and an NPC one level deeper than the
        // player it's supposed to sort identically against turned out
        // to actually draw wrong (NPCs rendering in front of tree
        // canopy the player correctly goes behind). Every character
        // now sits at one uniform depth under the world layer, no
        // exceptions, so there's nothing left to reason about there.
        parent.AddChild(actor); // runs NPCActor._Ready() synchronously (parent's already in the tree), which is what SetCharacterSprite() below needs to exist first

        var agent = new NpcAgent();
        parent.AddChild(agent); // a sibling of actor, not its parent — NpcAgent has no visual presence of its own, so it has no reason to be part of the render tree
        agent.Initialize(id, personality, actor, config.CreateProvider(), worldContext, thoughtLog, uiLog, config.PureLlmMode);

        actor.SetCharacterSprite(spriteVariant);
        actor.SetDisplayName(personality.Name);

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
