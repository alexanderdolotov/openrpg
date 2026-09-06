using Godot;

// Tiny shared-state autoload — carries the name entered on StartScreen
// through to Main. NOT the player character itself (see the README's
// architecture note on what that actually requires); this only exists
// because Godot has no built-in way to pass data across a scene change
// otherwise.
public partial class PlayerState : Node
{
    public string PlayerName = "";
}
