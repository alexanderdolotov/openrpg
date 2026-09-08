extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var wren = world_reg.GetEntity("Wren")
	var player = world_reg.GetEntity("player")
	var oak = main.get_node("WorldLayer/Oak0")
	var bush = main.get_node("WorldLayer/PlainBush0")

	# Build a deliberate "V pocket" wall directly between Wren and the
	# player — two obstacles close enough together that their inflated
	# (radius + NpcClearance) footprints overlap and fully block the
	# straight line, but with open ground above/below to route around.
	oak.global_position = Vector2(2000, 1985)
	bush.global_position = Vector2(2000, 2015)
	wren.global_position = Vector2(1900, 2000)
	player.global_position = Vector2(2100, 2000)
	main.__TestRebuildPathGrid()
	await process_frame

	print("start distance to player (expect 200): ", wren.global_position.distance_to(player.global_position))

	wren.__TestAssignFollow("player")

	# Sample progress every ~0.3s (the stuck-check's own interval) —
	# expect one or two near-zero intervals right as it first meets the
	# obstacle wall (stuck-check threshold is 120*0.3*0.35=12.6px), then
	# real movement again once the reactive A* reroute kicks in.
	var checkpoint = wren.global_position
	for i in range(360): # 6 real seconds at 60fps
		await physics_frame
		if i % 18 == 17:
			var pos = wren.global_position
			print("  t=", (i + 1) / 60.0, "s  progress this interval: ", pos.distance_to(checkpoint), "  pos: ", pos)
			checkpoint = pos

	var final_dist = wren.global_position.distance_to(player.global_position)
	print("final distance to player after 6s (expect <= Follow range 90, i.e. actually got there): ", final_dist)
	print("final wren position (expect roughly near (2100,2000), routed around the pocket): ", wren.global_position)

	quit()
