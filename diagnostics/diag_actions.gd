extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var panel = main.get_node("UI/ActionPanel")
	var active_label = panel.get_node("ActiveActionLabel")
	var pick_button = panel.get_node("PickAppleButton")

	print("ActiveActionLabel exists, initially hidden: ", active_label != null and not active_label.visible)

	# Move the player right next to Tree0 (600,120) so pick_apple becomes
	# available, then let one physics frame run RefreshActionPanel().
	player.position = Vector2(600, 150)
	await process_frame
	await physics_frame
	print("PickAppleButton visible once in range: ", pick_button.visible)
	print("PickAppleButton text shows a quick-key number: ", pick_button.text.begins_with("1. "))

	# Simulate a real click via the actual Godot signal (Button.pressed)
	# -- exercises the exact same TryAssign()/EnterActiveMode() path a
	# number-key press does.
	pick_button.emit_signal("pressed")
	await physics_frame

	print("")
	print("--- immediately after selecting the action ---")
	print("ActiveActionLabel visible: ", active_label.visible)
	print("ActiveActionLabel text: ", active_label.text)
	print("PickAppleButton hidden (panel frozen in active mode): ", not pick_button.visible)

	# Should NOT resolve instantly -- NPCActor.AttemptDuration (1.5s)
	# gates it. Check shortly after selecting: still not idle.
	await create_timer(0.5).timeout
	var still_busy_early = active_label.visible
	print("still showing active (not yet resolved) at +0.5s: ", still_busy_early)

	# Should be done well before, say, +4s (1.5s attempt duration plus
	# a little slack for the immediate-range case with no travel needed).
	await create_timer(3.5).timeout
	print("")
	print("--- well after AttemptDuration should have elapsed ---")
	print("ActiveActionLabel hidden again (action resolved, panel restored): ", not active_label.visible)

	# Space now pauses (moved off PlayerCharacter's old topmost-action
	# role) -- call Main's own override directly, same as simulating a
	# real keypress.
	var was_paused = main.get_tree().paused
	var space_event = InputEventKey.new()
	space_event.keycode = KEY_SPACE
	space_event.pressed = true
	main._unhandled_key_input(space_event)
	print("")
	print("Space toggles pause: was=%s now=%s (changed=%s)" % [was_paused, main.get_tree().paused, was_paused != main.get_tree().paused])
	# Unpause again so the rest of the test tree doesn't stay frozen.
	main._unhandled_key_input(space_event)

	quit()
