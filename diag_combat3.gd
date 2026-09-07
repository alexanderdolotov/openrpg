extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var panel = main.get_node("UI/ActionPanel")
	var eat_button = panel.get_node("EatButton")
	var pickup_button = panel.get_node("PickUpStickButton")
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var world_reg = main.get_node("/root/World")

	print("EatButton hidden at full health (correct gating): ", not eat_button.visible)

	# Stick pickup, via the real world-placed stick.
	var stick = world_reg.GetEntity("stick_0")
	print("stick_0 found: ", stick != null, " at ", (stick.position if stick else "n/a"))
	player.position = stick.position + Vector2(5, 5)
	await process_frame
	await physics_frame
	print("PickUpStickButton visible: ", pickup_button.visible)

	pickup_button.emit_signal("pressed")
	await create_timer(4.0).timeout
	print("inventory after pickup (should include 'stick'): ", inv_label.text)
	print("stick_0 gone from world (picked up): ", world_reg.GetEntity("stick_0") == null)

	quit()
