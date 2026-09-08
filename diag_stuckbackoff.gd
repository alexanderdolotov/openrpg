extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var wren = world_reg.GetEntity("Wren")
	var lake = main.get_node("MountainLake")

	# Dead center of the lake — guaranteed no walkable cell within
	# SnapToWalkable's search radius, so every FindPath call fails fast
	# (short-circuits before the real graph search) without needing to
	# construct a whole sealed-off pocket. The backoff logic being
	# tested only keys off "did FindPath return null", not why, so this
	# is a valid, cheap stand-in for "genuinely unreachable."
	wren.global_position = lake.global_position
	main.__TestRebuildPathGrid()
	await process_frame

	wren.__TestAssignFollow("player")

	for i in range(180): # 3 real seconds at 60fps
		await physics_frame

	var calls = main.__TestPathGridCallCount()
	print("FindPath calls over 3s while stuck+unreachable (expect ~2 with backoff, would be ~10 without): ", calls)
	print("PASS (backoff working): ", calls <= 4)

	quit()
