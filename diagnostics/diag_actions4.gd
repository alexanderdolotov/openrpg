extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var active_label = main.get_node("UI/ActionPanel/ActiveActionLabel")
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var log = main.get_node("UI/DebugLog")

	# Verify the FishIcon script identity properly (get_class() only
	# ever reports the built-in Godot base type, same as every other
	# custom C# script in this project).
	var spot = main.get_node("FishingSpot0")
	print("FishingSpot0 children with real script identity:")
	for c in spot.get_children():
		var s = c.get_script()
		print("  ", c.name, " script=", (s.resource_path if s else "none"), " visible=", c.visible)

	# Retry pick_apple up to 5 times (a "fumbled" roll is possible and
	# legitimate -- DifficultyClass.Gather is low but not zero) so a
	# single unlucky roll doesn't read as a bug.
	var key_event = InputEventKey.new()
	key_event.keycode = KEY_1
	key_event.pressed = true
	var got_apple = false
	for attempt in range(5):
		player.position = Vector2(600, 150)
		await process_frame
		await physics_frame
		player._unhandled_key_input(key_event)
		await create_timer(4.0).timeout
		if inv_label.text.contains("🍎"):
			got_apple = true
			break
	print("")
	print("InventoryLabel after up to 5 pick_apple attempts: ", inv_label.text)
	print("apple eventually showed up: ", got_apple)

	print("")
	print("full debug log bbcode text:")
	print(log.text)

	quit()
