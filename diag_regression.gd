extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	# Pause should still freeze the world layer (and everything under
	# it) while leaving Main/UI responsive -- this depends on
	# _worldLayer's ProcessMode override reaching every actual
	# character, which is now simpler (uniform direct children) but
	# worth re-confirming didn't regress.
	var world_layer = main.get_node("WorldLayer")
	print("WorldLayer process_mode (expect 1=PROCESS_MODE_PAUSABLE): ", world_layer.process_mode)
	var player = main.get_node("WorldLayer/Player")
	print("Player process_mode (expect 0=INHERIT, so it follows WorldLayer's Pausable): ", player.process_mode)

	# Exploration/generation still wires through the (now-simpler) tree
	# correctly.
	var before = 0
	for c in world_layer.get_children():
		var s = c.get_script()
		if s and s.resource_path.ends_with("AppleTree.cs"):
			before += 1
	player.position = Vector2(2600, 100)
	await create_timer(3.0).timeout
	var after = 0
	for c in world_layer.get_children():
		var s = c.get_script()
		if s and s.resource_path.ends_with("AppleTree.cs"):
			after += 1
	print("tree count before/after moving player far away: %d -> %d (generation still fires: %s)" % [before, after, str(after >= before)])

	quit()
