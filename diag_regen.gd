extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var world_reg = main.get_node("/root/World")
	var g0 = world_reg.GetEntity("grass_0")
	print("grass_0 initial Count (expect 3): ", g0.Count)

	# Deplete it.
	g0.AnimalEat()
	g0.AnimalEat()
	g0.AnimalEat()
	print("after 3 eats, Count (expect 0): ", g0.Count)
	print("4th eat succeeds (expect false): ", g0.AnimalEat())

	# Shrink RegenSeconds (an [Export] public field, settable from
	# GDScript) just for this test so it doesn't take 60 real seconds.
	g0.RegenSeconds = 0.3
	for i in range(60):
		await process_frame
	print("Count after waiting past the shortened regen window (expect 3): ", g0.Count)

	# Confirm it can be eaten again post-regen.
	print("eat succeeds after regen (expect true): ", g0.AnimalEat())

	quit()
