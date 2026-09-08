extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var world_reg = main.get_node("/root/World")
	var g0 = world_reg.GetEntity("grass_0")
	print("grass_0 initial Count: ", g0.Count)

	# Simulate 50 animals eating from the same patch (AnimalEat is public).
	var eaten = 0
	for i in range(50):
		if g0.AnimalEat():
			eaten += 1
	print("successful eats out of 50: ", eaten)
	print("Count remaining: ", g0.Count)
	print("still not depleted after 50 eats: ", g0.Count > 0)

	quit()
