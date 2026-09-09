extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var lake = main.get_node("MountainLake")
	var mountains = main.get_node("MistyMountains")
	print("Lake pos: ", lake.position, " Radius: ", lake.Radius)
	print("Mountains pos: ", mountains.position)

	# Mountains draws on top of the lake (later sibling = drawn later = in front).
	print("mountains sibling index: ", mountains.get_index(), " lake sibling index: ", lake.get_index())
	print("mountains draws in front of lake: ", mountains.get_index() > lake.get_index())

	# Real overlap check: lake's obstacle circle vs each mountain flank
	# circle — mirrored from MistyMountains.cs's own Flanks/BaseY (GDScript
	# can't call GetObstacleCircles() directly, it returns C# tuples).
	# Read the REAL CollisionShape2D children _Ready() actually built,
	# instead of recomputing Flanks by hand in GDScript.
	var lake_r = lake.Radius * 0.9
	var min_gap = 12.0
	var all_clear = true
	for c in mountains.get_children():
		if c is CollisionShape2D:
			var flank_world = mountains.position + c.position
			var radius = c.shape.radius
			var dist = lake.position.distance_to(flank_world)
			var needed = lake_r + radius + min_gap
			print("real flank shape at ", flank_world, " r=", radius, " dist to lake=", dist, " needed=", needed, " clear: ", dist >= needed)
			if dist < needed:
				all_clear = false
	print("lake clears every REAL mountain flank shape: ", all_clear)

	var foothills = main.get_node("Foothills")
	var forest = main.get_node("Forest")
	var dist_fh = lake.position.distance_to(foothills.position)
	var needed_fh = lake_r + foothills.Radius + 12.0
	print("Foothills dist=", dist_fh, " needed=", needed_fh, " clear: ", dist_fh >= needed_fh)
	var dist_forest = lake.position.distance_to(forest.position)
	var needed_forest = lake_r + forest.PatchRadius * 0.85 + 12.0
	print("Forest dist=", dist_forest, " needed=", needed_forest, " clear: ", dist_forest >= needed_forest)

	quit()
