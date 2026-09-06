using Godot;

// The C# equivalent of the try_interact() duck-typing every GDScript
// target implemented. Same idea, now checked at compile time instead of
// via HasMethod() at runtime — a target either implements this or the
// compiler stops you from wiring it up wrong.
public interface IInteractable
{
    InteractResult TryInteract(NPCActor actor, string actionId);
}

public readonly struct InteractResult
{
    public readonly bool Success;
    public readonly string Reason;
    public readonly Godot.Collections.Dictionary Data;

    public InteractResult(bool success, string reason, Godot.Collections.Dictionary data = null)
    {
        Success = success;
        Reason = reason;
        Data = data ?? new Godot.Collections.Dictionary();
    }
}
