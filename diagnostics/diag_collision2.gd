extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var foothills = main.get_node("Foothills")
	print("Foothills class: ", foothills.get_class())
	for c in foothills.get_children():
		if c.get_class() == "CollisionShape2D":
			print("  CollisionShape2D radius: ", c.shape.radius)

	var mountains = main.get_node("MistyMountains")
	print("MistyMountains class: ", mountains.get_class(), " (has physics body: ", mountains is StaticBody2D, ")")
	print("MistyMountains children: ", mountains.get_children().map(func(c): return c.get_class()))
	quit()
