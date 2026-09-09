extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame
	await physics_frame

	var player = main.get_node("WorldLayer/Player")
	var mountains_pos = main.get_node("MistyMountains").position
	print("MistyMountains position: ", mountains_pos)
	print("ActionRanges.Travel arrival radius should be 120")

	# Approach from due south (the natural direction from the village,
	# which sits south of the frontier) in small steps, using real
	# physics test_move at each step, to find the CLOSEST distance the
	# player can actually get to before being physically blocked.
	var closest_reachable = INF
	for dist in range(600, 0, -5):
		var pos = mountains_pos + Vector2(0, dist)
		var xform = Transform2D(0, pos)
		var blocked = player.test_move(xform, Vector2(0, -1))
		if not blocked:
			closest_reachable = dist
		else:
			print("first blocked at distance ", dist, " approaching from the south")
			break

	print("closest reachable distance from the south: ", closest_reachable)
	print("is that within Travel arrival range (120)?: ", closest_reachable <= 120)

	# Also check straight-line distance from several other directions,
	# in case south happens to be uniquely bad/good.
	for angle_deg in [0, 45, 90, 135, 180, 225, 270, 315]:
		var rad = deg_to_rad(angle_deg)
		var dir = Vector2(cos(rad), sin(rad))
		var closest = INF
		for dist in range(500, 0, -10):
			var pos = mountains_pos + dir * dist
			var xform = Transform2D(0, pos)
			var blocked = player.test_move(xform, -dir * 2)
			if not blocked:
				closest = dist
			else:
				break
		print("angle %d deg: closest reachable = %s (within 120: %s)" % [angle_deg, str(closest), str(closest <= 120)])

	quit()
