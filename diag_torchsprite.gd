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

	var torch_sprite = player.get_node("Sprite/TorchSprite")
	print("TorchSprite exists: ", torch_sprite != null)
	print("TorchSprite visible before crafting a torch (should be false): ", torch_sprite.visible)
	print("TorchSprite local position before facing right (should be +6,-9): ", torch_sprite.position)

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

	print("TorchSprite visible after crafting a torch (should be true): ", torch_sprite.visible)
	print("TorchLight visible after crafting (should also be true): ", player.get_node("TorchLight").visible)

	# Face left, then right, via real synthesized key input (PlayerCharacter
	# reads Input.IsKeyPressed each physics frame, so this exercises the
	# actual movement/UpdateSpriteFacing path, not a shortcut around it).
	var key_a = InputEventKey.new()
	key_a.keycode = KEY_A
	key_a.pressed = true
	Input.parse_input_event(key_a)
	await physics_frame
	await physics_frame
	print("Sprite.flip_h facing left: ", player.get_node("Sprite").flip_h)
	print("TorchSprite position facing left (X should be negative, -6,-9): ", torch_sprite.position)
	key_a.pressed = false
	Input.parse_input_event(key_a)

	var key_d = InputEventKey.new()
	key_d.keycode = KEY_D
	key_d.pressed = true
	Input.parse_input_event(key_d)
	await physics_frame
	await physics_frame
	print("Sprite.flip_h facing right: ", player.get_node("Sprite").flip_h)
	print("TorchSprite position facing right (X should be positive, 6,-9): ", torch_sprite.position)
	key_d.pressed = false
	Input.parse_input_event(key_d)

	quit()
