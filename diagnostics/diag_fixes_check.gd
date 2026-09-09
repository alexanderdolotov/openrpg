extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	# Fix #1: MountainLake should no longer overlap Foothills.
	var lake = main.get_node("MountainLake")
	var foothills = main.get_node("Foothills")
	var dist = lake.position.distance_to(foothills.position)
	var lake_r = lake.Radius * 0.9
	var foothills_r = foothills.Radius
	print("MountainLake pos: ", lake.position, " Foothills pos: ", foothills.position)
	print("distance: ", dist, " lake_r+foothills_r+gap(12): ", lake_r + foothills_r + 12.0)
	print("no longer overlapping (expect true): ", dist >= lake_r + foothills_r + 12.0)

	# Fix #3 sanity: the SpawnGrassPatch refactor should still produce
	# all 8 hand-placed, correctly-configured (walkable, no collision)
	# grass patches with no crash.
	var world_reg = main.get_node("/root/World")
	var all_present = true
	for i in range(8):
		var g = world_reg.GetEntity("grass_%d" % i)
		if g == null or g.get_shape_owners().size() != 0:
			all_present = false
	print("all 8 hand-placed grass patches present and walkable: ", all_present)

	quit()
