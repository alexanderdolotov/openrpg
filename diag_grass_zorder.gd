extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var world_layer = main.get_node("WorldLayer")
	var grass = main.get_node("Grass0")
	print("grass parent is Main directly (not WorldLayer): ", grass.get_parent() == main)
	print("grass sibling index: ", grass.get_index(), " WorldLayer sibling index: ", world_layer.get_index())
	print("grass draws behind WorldLayer (lower index): ", grass.get_index() < world_layer.get_index())

	# Sanity: still walkable/registered correctly after the AddBackgroundNode switch.
	var world_reg = main.get_node("/root/World")
	var g = world_reg.GetEntity("grass_0")
	print("grass_0 still registered: ", g != null, " same node: ", g == grass)
	print("grass shape owner count (should be 0): ", grass.get_shape_owners().size())

	quit()
