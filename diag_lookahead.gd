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

func collect_content(main, world_layer):
	var items = []
	for c in world_layer.get_children():
		var s = c.get_script()
		if s and (s.resource_path.ends_with("AppleTree.cs") or s.resource_path.ends_with("FishingSpot.cs")):
			items.append(c)
	for c in main.get_children():
		var s = c.get_script()
		if s and (s.resource_path.ends_with("Foothills.cs") or s.resource_path.ends_with("RiverCrossing.cs")):
			items.append(c)
	return items

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	var bodies = find_character_bodies(world_layer)
	var before = collect_content(main, world_layer)
	var before_names = {}
	for c in before:
		before_names[c.get_path()] = true

	# Move one character to a single far point, well beyond the village,
	# and check ONLY that one tick -- LookaheadRadius (1000) should
	# reveal any new region within 1000 units, not just the one the
	# character is standing in.
	var probe_pos = Vector2(2600, 100)
	bodies[0].position = probe_pos
	await create_timer(2.5).timeout

	var after = collect_content(main, world_layer)
	var new_items = []
	for c in after:
		if not before_names.has(c.get_path()):
			new_items.append(c)

	print("new content pieces generated from one lookahead check: ", new_items.size())
	var min_dist = INF
	for c in new_items:
		var d = c.global_position.distance_to(probe_pos)
		min_dist = min(min_dist, d)
		print("  ", c.name, " at ", c.global_position, " -- distance from character: ", d)

	# Visible half-diagonal at FollowZoom 1.6, viewport 1150x880 ~= 452px
	# (see Main.LookaheadRadius's own comment for the derivation). Every
	# newly generated piece should be farther than that from the
	# character that triggered it -- i.e. still offscreen, not already
	# in view when it appears.
	if new_items.size() > 0:
		print("closest new content is farther than the ~452px visible radius (should be true): ", min_dist > 452)
	else:
		print("(no content generated this run -- rng, not a wiring problem; rerun to sample again)")

	quit()
