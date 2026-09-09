extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var panel = main.get_node("UI/ActionPanel")
	var active_label = panel.get_node("ActiveActionLabel")
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")

	player.position = Vector2(600, 150) # right next to Tree0
	await process_frame
	await physics_frame

	var key_event = InputEventKey.new()
	key_event.keycode = KEY_1
	key_event.pressed = true
	player._unhandled_key_input(key_event)
	await physics_frame
	print("action selected: ", active_label.visible, " -- ", active_label.text)

	# Let the real attempt (1.5s hold + resolution) actually finish, via
	# the real game loop, no shortcuts.
	await create_timer(4.0).timeout
	print("action resolved, panel restored: ", not active_label.visible)
	print("InventoryLabel after a real pick_apple resolves: ", inv_label.text)

	print("")
	var spot = main.get_node("FishingSpot0")
	print("FishingSpot0 children:")
	for c in spot.get_children():
		print("  ", c.name, " : ", c.get_class(), " visible=", c.visible)

	quit()
