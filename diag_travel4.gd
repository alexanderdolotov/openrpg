extends SceneTree

func simulate(player, target, start_pos, speed, seconds):
	player.position = start_pos
	var closest = player.position.distance_to(target)
	for step in range(int(seconds * 60)):
		var dir = (target - player.position).normalized()
		player.velocity = dir * speed
		player.move_and_slide()
		await physics_frame
		closest = min(closest, player.position.distance_to(target))
		if closest <= 120.0:
			break
	return {"closest": closest, "final": player.position}

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var player = main.get_node("WorldLayer/Player")
	var mountains_pos = main.get_node("MistyMountains").position

	# South approach again (sanity re-check after the fix).
	var south = await simulate(player, mountains_pos, Vector2(700, 50), 160.0, 10.0)
	print("south approach: closest=%s reachable=%s" % [south.closest, south.closest <= 120.0])

	# Straight toward the left shoulder circle (-220, 120 local ->
	# global (480, -260)) from due south of THAT circle -- should get
	# solidly blocked well short of actually reaching it, proving
	# collision still exists there.
	var left_shoulder_global = mountains_pos + Vector2(-220, 120)
	var west = await simulate(player, left_shoulder_global, Vector2(480, 50), 160.0, 10.0)
	print("approach toward left shoulder circle: closest=%s (should NOT get all the way to 0, expect it to stop around the 110px radius): " % str(west.closest))
	print("  blocked well outside the circle's own radius (110): ", west.closest > 90.0)

	quit()
