extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var player = main.get_node("WorldLayer/Player")
	var mountains_pos = main.get_node("MistyMountains").position
	var speed = 160.0 # PlayerCharacter.FreeMoveSpeed -- close enough to NPCActor's own 120 for this check

	# Replicates exactly what NPCActor.ProcessNavigating() does each
	# physics frame when walking straight at a flagpole (travel never
	# uses PathGrid): Velocity = (target - position).normalized() *
	# Speed; MoveAndSlide(). Starting south of the frontier, same
	# realistic starting point as before.
	player.position = Vector2(700, 50)
	var start_dist = player.position.distance_to(mountains_pos)
	print("start distance: ", start_dist)

	var closest = start_dist
	# UnreachableTimeout is 10s; step at a real physics-frame delta (1/60)
	# rather than instantaneous jumps, so collision sliding behaves
	# exactly like it would in the real game.
	for step in range(600): # 10 seconds at 60fps
		var dir = (mountains_pos - player.position).normalized()
		player.velocity = dir * speed
		player.move_and_slide()
		await physics_frame
		var d = player.position.distance_to(mountains_pos)
		closest = min(closest, d)
		if d <= 120.0:
			break

	print("closest distance actually reached via real sliding movement: ", closest)
	print("got within Travel arrival range (120): ", closest <= 120.0)
	print("final position: ", player.position)

	quit()
