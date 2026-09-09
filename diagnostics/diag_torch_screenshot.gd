extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var world_reg = main.get_node("/root/World")
	var firepit = world_reg.GetEntity("firepit")
	var stick = world_reg.GetEntity("stick_0")
	var panel = main.get_node("UI/ActionPanel")
	var camera = player.get_node("Camera2D")

	# Get a stick, light the fire, make a torch — same flow diag_torchgive.gd uses.
	player.position = stick.position + Vector2(5, 5)
	await process_frame
	await physics_frame
	panel.get_node("PickUpStickButton").emit_signal("pressed")
	await create_timer(2.5).timeout

	player.position = firepit.position + Vector2(10, 0)
	await process_frame
	await physics_frame
	panel.get_node("LightFireButton").emit_signal("pressed")
	await create_timer(2.5).timeout
	panel.get_node("MakeTorchButton").emit_signal("pressed")
	await create_timer(2.5).timeout

	print("TorchSprite visible: ", player.get_node("Sprite/TorchSprite").visible)

	# Zoom the camera in tight on the player for a clear close-up crop,
	# well away from the firepit/stick so nothing else clutters the shot.
	player.position = Vector2(2000, 2000)
	camera.zoom = Vector2(8, 8)
	camera.reset_smoothing()
	for i in range(15):
		await process_frame
		await physics_frame

	var img = get_root().get_texture().get_image()
	img.save_png("res://../torch_screenshot.png")
	print("saved screenshot")

	quit()
