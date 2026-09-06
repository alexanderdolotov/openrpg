using Godot;

// The pre-launch name prompt. Its only job is collecting a name and
// handing it to the PlayerState autoload before switching to Main —
// nothing about the actual player character (movement, stats,
// inventory, IWorldCharacter) lives here; see PlayerCharacter for that.
public partial class StartScreen : Control
{
    private LineEdit _nameInput;
    private Label _hint;
    private Button _startButton;

    public override void _Ready()
    {
        _nameInput = GetNode<LineEdit>("NameInput");
        _hint = GetNode<Label>("Hint");
        _startButton = GetNode<Button>("StartButton");

        _nameInput.TextSubmitted += _ => TryStart();
        _startButton.Pressed += TryStart;
        _nameInput.GrabFocus();
    }

    private void TryStart()
    {
        string name = _nameInput.Text.Trim();
        if (name == "")
        {
            _hint.Text = "Type a name first.";
            _hint.Visible = true;
            return;
        }

        var state = GetNode<PlayerState>("/root/PlayerState");
        state.PlayerName = name;
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }
}
