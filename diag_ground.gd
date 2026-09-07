extends SceneTree

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

func count_by_script(parent, suffix):
	var n = 0
	for c in parent.get_children():
		var s = c.get_script()
		if s and s.resource_path.ends_with(suffix):
			n += 1
	return n

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var ground = main.get_node("Ground")
	print("Ground position: ", ground.position, " region_rect: ", ground.region_rect, " scale: ", ground.scale)
	# Reconstruct the world-space rect the sprite actually paints, the
	# same way BuildGround() computes it, and compare directly against
	# WorldExploration.MaxMapBounds by checking a few known corner/edge
	# world-points fall within [position, position + region_rect*scale).
	var painted_size = ground.region_rect.size * ground.scale
	var painted_rect = Rect2(ground.position, painted_size)
	print("painted world rect: ", painted_rect)

	# These are the exact MaxMapBounds corners from WorldExploration.cs
	# (new Rect2(-2000, -2500, 5500, 5000) -> spans x:[-2000,3500] y:[-2500,2500]).
	var expected = Rect2(-2000, -2500, 5500, 5000)
	print("expected MaxMapBounds rect: ", expected)
	print("painted rect matches expected (within 1px): ",
		abs(painted_rect.position.x - expected.position.x) < 1.0 and
		abs(painted_rect.position.y - expected.position.y) < 1.0 and
		abs(painted_rect.size.x - expected.size.x) < 1.0 and
		abs(painted_rect.size.y - expected.size.y) < 1.0)

	# Confirm exploration still actually fires -- i.e. StartingVillageArea
	# (not the new, much bigger GroundArea) is what got pre-marked
	# explored, so moving someone far out still counts as "new".
	var world_layer = main.get_node("WorldLayer")
	var bodies = find_character_bodies(world_layer)
	var before_total = count_by_script(world_layer, "AppleTree.cs") + count_by_script(world_layer, "FishingSpot.cs") \
		+ count_by_script(main, "Foothills.cs") + count_by_script(main, "RiverCrossing.cs")
	var targets = [Vector2(2600, 100), Vector2(2600, -1800), Vector2(-1600, 1800), Vector2(2600, 1800)]
	for i in range(bodies.size()):
		bodies[i].position = targets[i % targets.size()]
	await create_timer(3.0).timeout
	var after_total = count_by_script(world_layer, "AppleTree.cs") + count_by_script(world_layer, "FishingSpot.cs") \
		+ count_by_script(main, "Foothills.cs") + count_by_script(main, "RiverCrossing.cs")
	print("exploration still generates content after far movement: before=%d after=%d changed=%s" % [before_total, after_total, str(after_total != before_total)])

	quit()
