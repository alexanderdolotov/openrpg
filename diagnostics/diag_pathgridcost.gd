extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	print("BuildPathGrid at boot (small, real): ", main.__TestTimedBuildPathGrid(), "ms")

	# Simulate a well-explored session: scatter existing decor across
	# the full map bounds (cheap stand-in for real hours-long
	# exploration growth, without waiting for it).
	var forest = main.get_node("Forest")
	var foothills = main.get_node("Foothills")
	var mountains = main.get_node("MistyMountains")
	var lake = main.get_node("MountainLake")
	forest.global_position = Vector2(-1900, -2400)
	foothills.global_position = Vector2(3400, 2400)
	mountains.global_position = Vector2(3400, -2400)
	lake.global_position = Vector2(-1900, 2400)
	await process_frame

	var ms = main.__TestTimedBuildPathGrid()
	print("BuildPathGrid spanning the full map (worst case): ", ms, "ms")

	# Repeat a few times for a stable reading (first call sometimes
	# pays a one-off JIT/allocation cost).
	for i in range(5):
		ms = main.__TestTimedBuildPathGrid()
		print("  repeat: ", ms, "ms")

	quit()
