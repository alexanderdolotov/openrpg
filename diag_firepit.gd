extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var player = main.get_node("WorldLayer/Player")
	var firepit = world_reg.GetEntity("firepit")
	var stick = world_reg.GetEntity("stick_0")
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var panel = main.get_node("UI/ActionPanel")
	var light_fire_button = panel.get_node("LightFireButton")
	var make_torch_button = panel.get_node("MakeTorchButton")

	print("firepit found: ", firepit != null, " at ", (firepit.position if firepit else "n/a"))
	print("stick_0 found: ", stick != null)
	print("firepit.IsLit initially: ", firepit.IsLit)

	# Grab a stick first.
	player.position = stick.position + Vector2(5, 5)
	await process_frame
	await physics_frame
	var pickup_button = panel.get_node("PickUpStickButton")
	print("pickup button visible: ", pickup_button.visible)
	pickup_button.emit_signal("pressed")
	await create_timer(2.5).timeout
	print("inventory after stick pickup: ", inv_label.text)

	# Walk to the fire pit and light it.
	player.position = firepit.position + Vector2(10, 0)
	await process_frame
	await physics_frame
	print("light_fire button visible: ", light_fire_button.visible)
	light_fire_button.emit_signal("pressed")
	await create_timer(2.5).timeout
	print("firepit.IsLit after lighting: ", firepit.IsLit)

	await process_frame
	await physics_frame
	print("make_torch button visible: ", make_torch_button.visible)
	make_torch_button.emit_signal("pressed")
	await create_timer(2.5).timeout
	print("inventory after making torch: ", inv_label.text)

	quit()
