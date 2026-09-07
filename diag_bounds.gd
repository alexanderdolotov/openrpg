extends SceneTree

func count_by_script(parent, script_suffix):
	var n = 0
	for c in parent.get_children():
		var s = c.get_script()
		if s and s.resource_path.ends_with(script_suffix):
			n += 1
	return n

func find_character_bodies(world_layer):
	var bodies = []
	for c in world_layer.get_children():
		if c.get_class() == "CharacterBody2D":
			bodies.append(c)
		elif c.get_class() == "Node":
			for gc in c.get_children():
				if gc.get_class() == "CharacterBody2D":
					bodies.append(gc)
	return bodies

func _initialize():
	var main_scene = load("res://scenes/Main.tscn")
	var main = main_scene.instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	var before = count_by_script(world_layer, "AppleTree.cs") + count_by_script(world_layer, "FishingSpot.cs") \
		+ count_by_script(main, "Foothills.cs") + count_by_script(main, "RiverCrossing.cs")

	# Push everyone WAY outside WorldExploration.MaxMapBounds
	# (-2000,-2500,5500,5000 -> x up to 3500, y up to 2500).
	var bodies = find_character_bodies(world_layer)
	for b in bodies:
		b.position = Vector2(500000, 500000)

	await create_timer(3.0).timeout

	var after = count_by_script(world_layer, "AppleTree.cs") + count_by_script(world_layer, "FishingSpot.cs") \
		+ count_by_script(main, "Foothills.cs") + count_by_script(main, "RiverCrossing.cs")
	print("content count before=%d after=%d (should be equal — out of MaxMapBounds never generates)" % [before, after])
	quit()
