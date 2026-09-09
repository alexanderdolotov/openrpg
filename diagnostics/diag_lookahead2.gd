extends SceneTree

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
	var player = main.get_node("WorldLayer/Player")

	# Simulate REALISTIC gradual walking, not a teleport: move the
	# player in a straight line, in steps no bigger than a real
	# character could actually cover between two exploration-timer
	# ticks (player top speed ~160/s * 2s tick ~= 320, using 300 to stay
	# under that), waiting a real tick between each step -- this is what
	# actually happens during normal play, unlike a single instant jump
	# to a far point.
	var direction = Vector2(1, 0)
	var step = 300.0
	var worst_margin = INF
	var seen = {}
	for c in collect_content(main, world_layer):
		seen[c.get_path()] = true

	for i in range(10):
		player.position += direction * step
		await create_timer(2.3).timeout  # real tick (2.0) + small margin

		var current = collect_content(main, world_layer)
		for c in current:
			if not seen.has(c.get_path()):
				seen[c.get_path()] = true
				var d = c.global_position.distance_to(player.position)
				worst_margin = min(worst_margin, d)
				print("step %d: new '%s' appeared %.1f units from player (player at %s)" % [i, c.name, d, str(player.position)])

	print("")
	print("worst (smallest) distance from player to any newly-appeared content under realistic gradual movement: ", worst_margin)
	print("stays outside the ~452px visible radius the whole time: ", worst_margin > 452 if worst_margin != INF else "no content appeared this run (rng)")
	quit()
