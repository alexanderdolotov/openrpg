extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var panel = main.get_node("UI/ActionPanel")
	var active_label = panel.get_node("ActiveActionLabel")

	# Get within range of Tree0 and let the panel populate.
	player.position = Vector2(600, 150)
	await process_frame
	await physics_frame

	# Fire a raw "1" keypress the same way the OS would, straight at
	# PlayerCharacter's own _unhandled_key_input override.
	var key_event = InputEventKey.new()
	key_event.keycode = KEY_1
	key_event.pressed = true
	player._unhandled_key_input(key_event)
	await physics_frame

	print("pressing '1' selected the action (label showing): ", active_label.visible)
	print("label text: ", active_label.text)

	# --- Inventory label ---
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	print("")
	print("InventoryLabel initial text: ", inv_label.text)
	player.Inventory.Add("apple", 3)
	player.Inventory.Add("fish", 1)
	await create_timer(4.0).timeout  # let the current attempt resolve + a panel refresh happen
	print("InventoryLabel after adding items: ", inv_label.text)

	# --- Fish icon ---
	var spot = main.get_node("FishingSpot0")
	print("")
	print("FishingSpot0 children (should be water Sprite2D then FishIcon): ")
	for c in spot.get_children():
		print("  ", c.name, " : ", c.get_class(), " visible=", c.visible)

	quit()
