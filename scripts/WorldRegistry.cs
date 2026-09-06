using Godot;
using System.Collections.Generic;

// Autoloaded as "World" (see project.godot). Every interactable entity
// registers itself here under a stable string id. This is the ONLY
// vocabulary the mind layer is allowed to reference targets by — never
// raw node paths, never coordinates. If an id isn't registered, it
// doesn't exist as far as any action is concerned.
public partial class WorldRegistry : Node
{
    private readonly Dictionary<string, Node> _entities = new();

    public void Register(string id, Node node) => _entities[id] = node;

    public void Unregister(string id) => _entities.Remove(id);

    public Node GetEntity(string id)
    {
        if (!_entities.TryGetValue(id, out var node))
            return null;
        if (!IsInstanceValid(node))
        {
            _entities.Remove(id);
            return null;
        }
        return node;
    }

    public bool Exists(string id) => GetEntity(id) != null;
}
