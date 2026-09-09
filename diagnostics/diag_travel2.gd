extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var player = main.get_node("WorldLayer/Player")
	var mountains_pos = main.get_node("MistyMountains").position
	print("mountains at ", mountains_pos)

	# Start well south, roughly where the frontier obstacle course
	# begins in practice (per Main.BuildWorld's own comments, the
	# frontier sits around y -100 to -530) -- a realistic starting point
	# for a real "travel toward misty_mountains" attempt, not an
	# artificial probe position.
	player.position = Vector2(700, 50)

	var GameActionScript = load("res://scripts/GameAction.cs")
	var action = GameActionScript.new("travel", "misty_mountains", 120.0)
	player.AssignAction(action)

	var result = null
	var handler = func(r): result = r
	player.ActionCompleted.connect(handler)

	# UnreachableTimeout (10s) + AttemptDuration (1.5s) + real margin.
	for i in range(16):
		await create_timer(1.0).timeout
		if result != null:
			break

	if result == null:
		print("FAILED: action never completed within 16 seconds")
	else:
		print("action completed: success=%s reason=%s" % [result["success"], result["reason"]])
		print("final player position: ", player.position, " distance from mountains: ", player.position.distance_to(mountains_pos))

	quit()
